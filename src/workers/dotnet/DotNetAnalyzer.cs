using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using AiContextBuilder.Core.Protocol;
using ProtocolDiagnostic = AiContextBuilder.Core.Protocol.Diagnostic;
using ProtocolSeverity = AiContextBuilder.Core.Protocol.DiagnosticSeverity;

namespace AiContextBuilder.Workers.DotNet;

public class DotNetAnalyzer
{
    public DependencyGraphFragment Analyze(
        string rootFilePath,
        string workspaceRoot,
        UserConfiguration config)
    {
        var fragment = new DependencyGraphFragment();
        string normalizedRoot = Path.GetFullPath(rootFilePath);

        if (!File.Exists(normalizedRoot))
        {
            fragment.Diagnostics.Add(new ProtocolDiagnostic
            {
                Code = "FileNotFound",
                Severity = ProtocolSeverity.Fatal,
                Message = $"Source file not found: {normalizedRoot}",
                RelativePath = Path.GetRelativePath(workspaceRoot, normalizedRoot),
                Adapter = "DotNetAnalyzer"
            });
            return fragment;
        }

        var visitedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var filesToAnalyze = new Queue<string>();

        filesToAnalyze.Enqueue(normalizedRoot);
        visitedFiles.Add(normalizedRoot);

        // Map of type name to declaring file paths for project-level symbol resolution
        var workspaceFiles = Directory.Exists(workspaceRoot)
            ? Directory.GetFiles(workspaceRoot, "*.cs", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(workspaceRoot, "*.razor", SearchOption.AllDirectories))
                .Concat(Directory.GetFiles(workspaceRoot, "*.cshtml", SearchOption.AllDirectories))
                .ToList()
            : new List<string> { normalizedRoot };

        var typeToFileIndex = BuildTypeToFileIndex(workspaceFiles, workspaceRoot);

        while (filesToAnalyze.Count > 0)
        {
            string currentFilePath = filesToAnalyze.Dequeue();
            string relPath = Path.GetRelativePath(workspaceRoot, currentFilePath).Replace('\\', '/');

            string ext = Path.GetExtension(currentFilePath).ToLowerInvariant();
            if (ext is ".razor" or ".cshtml")
            {
                AnalyzeRazorOrBlazor(currentFilePath, relPath, workspaceRoot, fragment, filesToAnalyze, visitedFiles);
                continue;
            }

            if (ext == ".cs")
            {
                AnalyzeCSharpFile(currentFilePath, relPath, workspaceRoot, fragment, filesToAnalyze, visitedFiles, typeToFileIndex, config);
            }
        }

        return fragment;
    }

    private void AnalyzeCSharpFile(
        string filePath,
        string relPath,
        string workspaceRoot,
        DependencyGraphFragment fragment,
        Queue<string> queue,
        HashSet<string> visitedFiles,
        Dictionary<string, List<string>> typeToFileIndex,
        UserConfiguration config)
    {
        string code = File.ReadAllText(filePath);
        var tree = CSharpSyntaxTree.ParseText(code, path: filePath);
        var root = tree.GetCompilationUnitRoot();

        string fileNodeId = $"file:{relPath}";
        var fileNode = new GraphNode
        {
            Id = fileNodeId,
            Kind = NodeKind.File,
            Language = "CSharp",
            AnalysisLevel = CapabilityLevel.Semantic,
            DisplayName = Path.GetFileName(filePath),
            QualifiedName = relPath,
            RelativePath = relPath,
            SourceAvailable = true,
            Content = code
        };
        fragment.Nodes.Add(fileNode);

        // Namespace discovery
        var namespaces = root.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>().ToList();
        string currentNamespace = namespaces.FirstOrDefault()?.Name.ToString() ?? string.Empty;

        // Discovered declared types (Classes, Interfaces, Records, Structs, Enums)
        var typeDeclarations = root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>().ToList();

        foreach (var typeDecl in typeDeclarations)
        {
            string typeName = typeDecl.Identifier.Text;
            string qualifiedName = string.IsNullOrEmpty(currentNamespace) ? typeName : $"{currentNamespace}.{typeName}";
            string typeNodeId = $"type:{relPath}#{qualifiedName}";

            NodeKind kind = typeDecl switch
            {
                ClassDeclarationSyntax => NodeKind.Class,
                InterfaceDeclarationSyntax => NodeKind.Interface,
                RecordDeclarationSyntax => NodeKind.Record,
                StructDeclarationSyntax => NodeKind.Struct,
                EnumDeclarationSyntax => NodeKind.Enum,
                _ => NodeKind.Type
            };

            var typeNode = new GraphNode
            {
                Id = typeNodeId,
                Kind = kind,
                Language = "CSharp",
                AnalysisLevel = CapabilityLevel.Semantic,
                DisplayName = typeName,
                QualifiedName = qualifiedName,
                RelativePath = relPath,
                SourceAvailable = true
            };
            fragment.Nodes.Add(typeNode);

            // Connect file -> type
            fragment.Edges.Add(new GraphEdge
            {
                SourceNodeId = fileNodeId,
                TargetNodeId = typeNodeId,
                Relationship = RelationshipType.Export,
                Confidence = Confidence.Verified,
                AnalysisLevel = CapabilityLevel.Semantic
            });

            // 1. Base types and implemented interfaces
            if (typeDecl.BaseList != null)
            {
                foreach (var baseType in typeDecl.BaseList.Types)
                {
                    string refName = GetSimpleTypeName(baseType.Type);
                    bool isInterface = refName.StartsWith("I") && refName.Length > 1 && char.IsUpper(refName[1]);
                    var rel = isInterface ? RelationshipType.Interface : RelationshipType.BaseType;

                    ResolveAndLinkType(typeNodeId, refName, rel, fragment, queue, visitedFiles, typeToFileIndex, workspaceRoot);
                }
            }

            // 2. Constructors & Injected Services
            var constructors = typeDecl.DescendantNodes().OfType<ConstructorDeclarationSyntax>();
            foreach (var ctor in constructors)
            {
                foreach (var param in ctor.ParameterList.Parameters)
                {
                    if (param.Type != null)
                    {
                        string paramTypeName = GetSimpleTypeName(param.Type);
                        ResolveAndLinkType(typeNodeId, paramTypeName, RelationshipType.ConstructorDependency, fragment, queue, visitedFiles, typeToFileIndex, workspaceRoot);
                    }
                }
            }

            // 3. Properties
            var properties = typeDecl.DescendantNodes().OfType<PropertyDeclarationSyntax>();
            foreach (var prop in properties)
            {
                string propTypeName = GetSimpleTypeName(prop.Type);
                ResolveAndLinkType(typeNodeId, propTypeName, RelationshipType.PropertyType, fragment, queue, visitedFiles, typeToFileIndex, workspaceRoot);
            }

            // 4. Fields
            var fields = typeDecl.DescendantNodes().OfType<FieldDeclarationSyntax>();
            foreach (var field in fields)
            {
                string fieldTypeName = GetSimpleTypeName(field.Declaration.Type);
                ResolveAndLinkType(typeNodeId, fieldTypeName, RelationshipType.FieldType, fragment, queue, visitedFiles, typeToFileIndex, workspaceRoot);
            }

            // 5. Methods (Parameters, Return Types)
            var methods = typeDecl.DescendantNodes().OfType<MethodDeclarationSyntax>();
            foreach (var method in methods)
            {
                if (method.ReturnType != null && method.ReturnType.ToString() != "void" && method.ReturnType.ToString() != "Task")
                {
                    string retTypeName = GetSimpleTypeName(method.ReturnType);
                    ResolveAndLinkType(typeNodeId, retTypeName, RelationshipType.ReturnType, fragment, queue, visitedFiles, typeToFileIndex, workspaceRoot);
                }

                foreach (var param in method.ParameterList.Parameters)
                {
                    if (param.Type != null)
                    {
                        string paramTypeName = GetSimpleTypeName(param.Type);
                        ResolveAndLinkType(typeNodeId, paramTypeName, RelationshipType.ParameterType, fragment, queue, visitedFiles, typeToFileIndex, workspaceRoot);
                    }
                }
            }

            // 6. Object Creations (new T())
            var creations = typeDecl.DescendantNodes().OfType<ObjectCreationExpressionSyntax>();
            foreach (var creation in creations)
            {
                string creationTypeName = GetSimpleTypeName(creation.Type);
                ResolveAndLinkType(typeNodeId, creationTypeName, RelationshipType.ObjectCreation, fragment, queue, visitedFiles, typeToFileIndex, workspaceRoot);
            }

            // 7. Generic Arguments (<T>)
            var genericNodes = typeDecl.DescendantNodes().OfType<GenericNameSyntax>();
            foreach (var gen in genericNodes)
            {
                foreach (var arg in gen.TypeArgumentList.Arguments)
                {
                    string argName = GetSimpleTypeName(arg);
                    ResolveAndLinkType(typeNodeId, argName, RelationshipType.GenericArgument, fragment, queue, visitedFiles, typeToFileIndex, workspaceRoot);
                }
            }

            // 8. Attributes
            if (config.IncludeAttributes)
            {
                foreach (var attrList in typeDecl.AttributeLists)
                {
                    foreach (var attr in attrList.Attributes)
                    {
                        string attrName = attr.Name.ToString();
                        ResolveAndLinkType(typeNodeId, attrName, RelationshipType.Attribute, fragment, queue, visitedFiles, typeToFileIndex, workspaceRoot);
                    }
                }
            }

            // 9. Partial classes in other files
            if (typeDecl.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)))
            {
                if (typeToFileIndex.TryGetValue(typeName, out var matchingFiles))
                {
                    foreach (var partialFile in matchingFiles)
                    {
                        if (!string.Equals(partialFile, filePath, StringComparison.OrdinalIgnoreCase))
                        {
                            string partialRel = Path.GetRelativePath(workspaceRoot, partialFile).Replace('\\', '/');
                            string partialFileId = $"file:{partialRel}";

                            fragment.Edges.Add(new GraphEdge
                            {
                                SourceNodeId = fileNodeId,
                                TargetNodeId = partialFileId,
                                Relationship = RelationshipType.PartialDeclaration,
                                Confidence = Confidence.Verified,
                                AnalysisLevel = CapabilityLevel.Semantic
                            });

                            if (visitedFiles.Add(partialFile))
                            {
                                queue.Enqueue(partialFile);
                            }
                        }
                    }
                }
            }
        }
    }

    private void AnalyzeRazorOrBlazor(
        string filePath,
        string relPath,
        string workspaceRoot,
        DependencyGraphFragment fragment,
        Queue<string> queue,
        HashSet<string> visitedFiles)
    {
        string content = File.ReadAllText(filePath);
        string fileNodeId = $"file:{relPath}";

        fragment.Nodes.Add(new GraphNode
        {
            Id = fileNodeId,
            Kind = NodeKind.Component,
            Language = "Razor",
            Framework = "Blazor",
            AnalysisLevel = CapabilityLevel.SyntaxAware,
            DisplayName = Path.GetFileName(filePath),
            QualifiedName = relPath,
            RelativePath = relPath,
            SourceAvailable = true,
            Content = content
        });

        // 1. Companion Code-Behind (.razor.cs or .cshtml.cs)
        string codeBehindPath = filePath + ".cs";
        if (File.Exists(codeBehindPath))
        {
            string cbRel = Path.GetRelativePath(workspaceRoot, codeBehindPath).Replace('\\', '/');
            string cbId = $"file:{cbRel}";

            fragment.Edges.Add(new GraphEdge
            {
                SourceNodeId = fileNodeId,
                TargetNodeId = cbId,
                Relationship = RelationshipType.CodeBehind,
                Confidence = Confidence.Verified,
                AnalysisLevel = CapabilityLevel.CompanionFile
            });

            if (visitedFiles.Add(codeBehindPath)) queue.Enqueue(codeBehindPath);
        }

        // 2. Scoped CSS companion (.razor.css)
        string scopedCssPath = Path.ChangeExtension(filePath, ".razor.css");
        if (File.Exists(scopedCssPath))
        {
            string cssRel = Path.GetRelativePath(workspaceRoot, scopedCssPath).Replace('\\', '/');
            string cssId = $"file:{cssRel}";

            if (!fragment.Nodes.Any(n => n.Id == cssId))
            {
                fragment.Nodes.Add(new GraphNode
                {
                    Id = cssId,
                    Kind = NodeKind.Style,
                    Language = "CSS",
                    Framework = "Blazor",
                    AnalysisLevel = CapabilityLevel.CompanionFile,
                    DisplayName = Path.GetFileName(scopedCssPath),
                    QualifiedName = cssRel,
                    RelativePath = cssRel,
                    SourceAvailable = true,
                    Content = File.ReadAllText(scopedCssPath)
                });
            }

            fragment.Edges.Add(new GraphEdge
            {
                SourceNodeId = fileNodeId,
                TargetNodeId = cssId,
                Relationship = RelationshipType.Style,
                Confidence = Confidence.Verified,
                AnalysisLevel = CapabilityLevel.CompanionFile
            });
        }

        // 3. Companion JS module (.razor.js)
        string jsModulePath = Path.ChangeExtension(filePath, ".razor.js");
        if (File.Exists(jsModulePath))
        {
            string jsRel = Path.GetRelativePath(workspaceRoot, jsModulePath).Replace('\\', '/');
            string jsId = $"file:{jsRel}";

            if (!fragment.Nodes.Any(n => n.Id == jsId))
            {
                fragment.Nodes.Add(new GraphNode
                {
                    Id = jsId,
                    Kind = NodeKind.Module,
                    Language = "JavaScript",
                    Framework = "Blazor",
                    AnalysisLevel = CapabilityLevel.CompanionFile,
                    DisplayName = Path.GetFileName(jsModulePath),
                    QualifiedName = jsRel,
                    RelativePath = jsRel,
                    SourceAvailable = true,
                    Content = File.ReadAllText(jsModulePath)
                });
            }

            fragment.Edges.Add(new GraphEdge
            {
                SourceNodeId = fileNodeId,
                TargetNodeId = jsId,
                Relationship = RelationshipType.ScriptReference,
                Confidence = Confidence.Verified,
                AnalysisLevel = CapabilityLevel.CompanionFile
            });
        }

        // 4. Injected Services (@inject IService Service)
        var injectMatches = System.Text.RegularExpressions.Regex.Matches(content, @"@inject\s+([A-Za-z0-9_<>.]+)\s+([A-Za-z0-9_]+)");
        foreach (System.Text.RegularExpressions.Match m in injectMatches)
        {
            string injectedType = m.Groups[1].Value;
            string simpleType = injectedType.Split('<', '>')[0].Split('.').Last();

            string? targetFile = FindFileForType(simpleType, workspaceRoot);
            if (targetFile != null)
            {
                string targetRel = Path.GetRelativePath(workspaceRoot, targetFile).Replace('\\', '/');
                string targetId = $"file:{targetRel}";

                fragment.Edges.Add(new GraphEdge
                {
                    SourceNodeId = fileNodeId,
                    TargetNodeId = targetId,
                    Relationship = RelationshipType.Service,
                    Confidence = Confidence.Verified,
                    AnalysisLevel = CapabilityLevel.SyntaxAware
                });

                if (visitedFiles.Add(targetFile)) queue.Enqueue(targetFile);
            }
        }
    }

    private void ResolveAndLinkType(
        string sourceNodeId,
        string typeName,
        RelationshipType relationship,
        DependencyGraphFragment fragment,
        Queue<string> queue,
        HashSet<string> visitedFiles,
        Dictionary<string, List<string>> typeToFileIndex,
        string workspaceRoot)
    {
        if (IsBuiltInSystemType(typeName)) return;

        if (typeToFileIndex.TryGetValue(typeName, out var targetFiles))
        {
            foreach (var targetFile in targetFiles)
            {
                string targetRel = Path.GetRelativePath(workspaceRoot, targetFile).Replace('\\', '/');
                string targetNodeId = $"file:{targetRel}";

                fragment.Edges.Add(new GraphEdge
                {
                    SourceNodeId = sourceNodeId,
                    TargetNodeId = targetNodeId,
                    Relationship = relationship,
                    Confidence = Confidence.Verified,
                    AnalysisLevel = CapabilityLevel.Semantic
                });

                if (visitedFiles.Add(targetFile))
                {
                    queue.Enqueue(targetFile);
                }
            }
        }
    }

    private string GetSimpleTypeName(TypeSyntax typeSyntax)
    {
        if (typeSyntax is GenericNameSyntax gen)
        {
            return gen.Identifier.Text;
        }
        if (typeSyntax is NullableTypeSyntax n)
        {
            return GetSimpleTypeName(n.ElementType);
        }
        if (typeSyntax is ArrayTypeSyntax a)
        {
            return GetSimpleTypeName(a.ElementType);
        }
        if (typeSyntax is QualifiedNameSyntax q)
        {
            return q.Right.Identifier.Text;
        }
        return typeSyntax.ToString().Trim('?', '[', ']');
    }

    private Dictionary<string, List<string>> BuildTypeToFileIndex(List<string> files, string workspaceRoot)
    {
        var index = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            string ext = Path.GetExtension(file).ToLowerInvariant();
            if (ext == ".cs")
            {
                try
                {
                    string text = File.ReadAllText(file);
                    var matches = System.Text.RegularExpressions.Regex.Matches(
                        text,
                        @"\b(?:class|interface|record|struct|enum)\s+([A-Za-z0-9_]+)");

                    foreach (System.Text.RegularExpressions.Match m in matches)
                    {
                        string name = m.Groups[1].Value;
                        if (!index.TryGetValue(name, out var list))
                        {
                            list = new List<string>();
                            index[name] = list;
                        }
                        list.Add(file);
                    }
                }
                catch { }
            }
            else if (ext is ".razor" or ".cshtml")
            {
                string componentName = Path.GetFileNameWithoutExtension(file);
                if (!index.TryGetValue(componentName, out var list))
                {
                    list = new List<string>();
                    index[componentName] = list;
                }
                list.Add(file);
            }
        }
        return index;
    }

    private string? FindFileForType(string typeName, string workspaceRoot)
    {
        string cs = Path.Combine(workspaceRoot, $"{typeName}.cs");
        if (File.Exists(cs)) return cs;

        string razor = Path.Combine(workspaceRoot, $"{typeName}.razor");
        if (File.Exists(razor)) return razor;

        if (Directory.Exists(workspaceRoot))
        {
            var matchCs = Directory.GetFiles(workspaceRoot, $"{typeName}.cs", SearchOption.AllDirectories).FirstOrDefault();
            if (matchCs != null) return matchCs;

            var matchRazor = Directory.GetFiles(workspaceRoot, $"{typeName}.razor", SearchOption.AllDirectories).FirstOrDefault();
            if (matchRazor != null) return matchRazor;
        }

        return null;
    }

    private bool IsBuiltInSystemType(string name)
    {
        return name is "string" or "int" or "bool" or "void" or "long" or "double" or
                       "float" or "decimal" or "char" or "byte" or "object" or "Task" or
                       "ValueTask" or "IEnumerable" or "List" or "Dictionary" or "HashSet" or
                       "Action" or "Func" or "IDisposable" or "DateTime" or "Guid" or
                       "CancellationToken";
    }
}
