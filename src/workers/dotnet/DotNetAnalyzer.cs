using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using AiPromptContextBuilder.Core.Protocol;
using ProtocolDiagnostic = AiPromptContextBuilder.Core.Protocol.Diagnostic;
using ProtocolSeverity = AiPromptContextBuilder.Core.Protocol.DiagnosticSeverity;

namespace AiPromptContextBuilder.Workers.DotNet;

public class ExtensionMethodInfo
{
    public string MethodName { get; set; } = string.Empty;
    public string DeclaringClassName { get; set; } = string.Empty;
    public string DeclaringFilePath { get; set; } = string.Empty;
    public string TargetTypeName { get; set; } = string.Empty;
    public string Namespace { get; set; } = string.Empty;
}

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
            ? Directory.GetFiles(workspaceRoot, "*.*", SearchOption.AllDirectories)
                .Where(f =>
                {
                    string fExt = Path.GetExtension(f).ToLowerInvariant();
                    if (fExt is not (".cs" or ".razor" or ".cshtml")) return false;
                    string norm = f.Replace('\\', '/');
                    return !norm.Contains("/bin/") && !norm.Contains("/obj/") &&
                           !norm.Contains("/.git/") && !norm.Contains("/.vs/");
                })
                .ToList()
            : new List<string> { normalizedRoot };

        var typeToFileIndex = BuildTypeToFileIndex(workspaceFiles, workspaceRoot);
        var extensionMethodIndex = BuildExtensionMethodIndex(workspaceFiles, workspaceRoot);

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
                AnalyzeCSharpFile(currentFilePath, relPath, workspaceRoot, fragment, filesToAnalyze, visitedFiles, typeToFileIndex, extensionMethodIndex, config);
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
        Dictionary<string, List<ExtensionMethodInfo>> extensionMethodIndex,
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

        // Static and aliased usings (e.g. using static CommonValues; using C = CommonValues;)
        foreach (var u in root.Usings)
        {
            if (u.StaticKeyword.IsKind(SyntaxKind.StaticKeyword) && u.Name != null)
            {
                string staticTypeName = u.Name.ToString().Split('.').Last();
                ResolveAndLinkType(fileNodeId, staticTypeName, RelationshipType.Import, fragment, queue, visitedFiles, typeToFileIndex, workspaceRoot, filePath);
            }
            else if (u.Alias != null && u.Name != null)
            {
                string aliasTypeName = u.Name.ToString().Split('.').Last();
                ResolveAndLinkType(fileNodeId, aliasTypeName, RelationshipType.Import, fragment, queue, visitedFiles, typeToFileIndex, workspaceRoot, filePath);
            }
        }

        // Discovered declared types (Classes, Interfaces, Records, Structs, Enums)
        var typeDeclarations = root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>().ToList();

        if (typeDeclarations.Count == 0)
        {
            // Top-level statements or file-scoped code
            ExtractDependenciesFromContainer(root, fileNodeId, filePath, workspaceRoot, fragment, queue, visitedFiles, typeToFileIndex, extensionMethodIndex, config);
        }
        else
        {
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

                        ResolveAndLinkType(typeNodeId, refName, rel, fragment, queue, visitedFiles, typeToFileIndex, workspaceRoot, filePath);
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
                            ResolveAndLinkType(typeNodeId, paramTypeName, RelationshipType.ConstructorDependency, fragment, queue, visitedFiles, typeToFileIndex, workspaceRoot, filePath);
                        }
                    }
                }

                // 3. Properties
                var properties = typeDecl.DescendantNodes().OfType<PropertyDeclarationSyntax>();
                foreach (var prop in properties)
                {
                    string propTypeName = GetSimpleTypeName(prop.Type);
                    ResolveAndLinkType(typeNodeId, propTypeName, RelationshipType.PropertyType, fragment, queue, visitedFiles, typeToFileIndex, workspaceRoot, filePath);
                }

                // 4. Fields
                var fields = typeDecl.DescendantNodes().OfType<FieldDeclarationSyntax>();
                foreach (var field in fields)
                {
                    string fieldTypeName = GetSimpleTypeName(field.Declaration.Type);
                    ResolveAndLinkType(typeNodeId, fieldTypeName, RelationshipType.FieldType, fragment, queue, visitedFiles, typeToFileIndex, workspaceRoot, filePath);
                }

                // 5. Methods (Parameters, Return Types)
                var methods = typeDecl.DescendantNodes().OfType<MethodDeclarationSyntax>();
                foreach (var method in methods)
                {
                    if (method.ReturnType != null && method.ReturnType.ToString() != "void" && method.ReturnType.ToString() != "Task")
                    {
                        string retTypeName = GetSimpleTypeName(method.ReturnType);
                        ResolveAndLinkType(typeNodeId, retTypeName, RelationshipType.ReturnType, fragment, queue, visitedFiles, typeToFileIndex, workspaceRoot, filePath);
                    }

                    foreach (var param in method.ParameterList.Parameters)
                    {
                        if (param.Type != null)
                        {
                            string paramTypeName = GetSimpleTypeName(param.Type);
                            ResolveAndLinkType(typeNodeId, paramTypeName, RelationshipType.ParameterType, fragment, queue, visitedFiles, typeToFileIndex, workspaceRoot, filePath);
                        }
                    }
                }

                // 6. Attributes
                if (config.IncludeAttributes)
                {
                    foreach (var attrList in typeDecl.AttributeLists)
                    {
                        foreach (var attr in attrList.Attributes)
                        {
                            string attrName = attr.Name.ToString();
                            ResolveAndLinkType(typeNodeId, attrName, RelationshipType.Attribute, fragment, queue, visitedFiles, typeToFileIndex, workspaceRoot, filePath);
                        }
                    }
                }

                // 7. Partial classes in other files
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

                // 8. Comprehensive expressions & member references inside type (constants, static classes, enums, casts, locals, new T())
                ExtractDependenciesFromContainer(typeDecl, typeNodeId, filePath, workspaceRoot, fragment, queue, visitedFiles, typeToFileIndex, extensionMethodIndex, config);
            }
        }
    }

    private void ExtractDependenciesFromContainer(
        SyntaxNode container,
        string sourceNodeId,
        string filePath,
        string workspaceRoot,
        DependencyGraphFragment fragment,
        Queue<string> queue,
        HashSet<string> visitedFiles,
        Dictionary<string, List<string>> typeToFileIndex,
        Dictionary<string, List<ExtensionMethodInfo>> extensionMethodIndex,
        UserConfiguration config)
    {
        // 1. Member Access Expressions (Constants, Enums, Static Methods/Fields, e.g. CommonValues.Fan_Name)
        var memberAccesses = container.DescendantNodes().OfType<MemberAccessExpressionSyntax>();
        foreach (var ma in memberAccesses)
        {
            foreach (var targetName in ExtractTargetTypeNames(ma.Expression))
            {
                var rel = (ma.Parent is InvocationExpressionSyntax)
                    ? RelationshipType.MethodCall
                    : RelationshipType.FieldType;
                ResolveAndLinkType(sourceNodeId, targetName, rel, fragment, queue, visitedFiles, typeToFileIndex, workspaceRoot, filePath);
            }
        }

        // 2. Conditional Access Expressions (e.g. CommonValues?.Fan_Name)
        var condAccesses = container.DescendantNodes().OfType<ConditionalAccessExpressionSyntax>();
        foreach (var ca in condAccesses)
        {
            foreach (var targetName in ExtractTargetTypeNames(ca.Expression))
            {
                ResolveAndLinkType(sourceNodeId, targetName, RelationshipType.FieldType, fragment, queue, visitedFiles, typeToFileIndex, workspaceRoot, filePath);
            }
        }

        // 3. Local Variable Declarations (e.g. CommonValues cv = ...)
        var varDecls = container.DescendantNodes().OfType<VariableDeclarationSyntax>();
        foreach (var vd in varDecls)
        {
            string vTypeName = GetSimpleTypeName(vd.Type);
            if (vTypeName != "var")
            {
                ResolveAndLinkType(sourceNodeId, vTypeName, RelationshipType.LocalType, fragment, queue, visitedFiles, typeToFileIndex, workspaceRoot, filePath);
            }
        }

        // 4. Object Creations (new T())
        var creations = container.DescendantNodes().OfType<ObjectCreationExpressionSyntax>();
        foreach (var creation in creations)
        {
            string creationTypeName = GetSimpleTypeName(creation.Type);
            ResolveAndLinkType(sourceNodeId, creationTypeName, RelationshipType.ObjectCreation, fragment, queue, visitedFiles, typeToFileIndex, workspaceRoot, filePath);
        }

        // 5. Generic Arguments (<T>)
        var genericNodes = container.DescendantNodes().OfType<GenericNameSyntax>();
        foreach (var gen in genericNodes)
        {
            foreach (var arg in gen.TypeArgumentList.Arguments)
            {
                string argName = GetSimpleTypeName(arg);
                ResolveAndLinkType(sourceNodeId, argName, RelationshipType.GenericArgument, fragment, queue, visitedFiles, typeToFileIndex, workspaceRoot, filePath);
            }
        }

        // 6. Cast Expressions ((CommonValues)obj)
        var casts = container.DescendantNodes().OfType<CastExpressionSyntax>();
        foreach (var c in casts)
        {
            string cTypeName = GetSimpleTypeName(c.Type);
            ResolveAndLinkType(sourceNodeId, cTypeName, RelationshipType.ConversionType, fragment, queue, visitedFiles, typeToFileIndex, workspaceRoot, filePath);
        }

        // 7. Type Pattern Matching, is/as expressions
        var binaryExpressions = container.DescendantNodes().OfType<BinaryExpressionSyntax>();
        foreach (var bin in binaryExpressions)
        {
            if (bin.IsKind(SyntaxKind.IsExpression) || bin.IsKind(SyntaxKind.AsExpression))
            {
                if (bin.Right is TypeSyntax rt)
                {
                    string tName = GetSimpleTypeName(rt);
                    ResolveAndLinkType(sourceNodeId, tName, RelationshipType.LocalType, fragment, queue, visitedFiles, typeToFileIndex, workspaceRoot, filePath);
                }
            }
        }

        var isPatterns = container.DescendantNodes().OfType<IsPatternExpressionSyntax>();
        foreach (var ip in isPatterns)
        {
            if (ip.Pattern is DeclarationPatternSyntax dec)
            {
                string pType = GetSimpleTypeName(dec.Type);
                ResolveAndLinkType(sourceNodeId, pType, RelationshipType.LocalType, fragment, queue, visitedFiles, typeToFileIndex, workspaceRoot, filePath);
            }
            else if (ip.Pattern is TypePatternSyntax tp)
            {
                string pType = GetSimpleTypeName(tp.Type);
                ResolveAndLinkType(sourceNodeId, pType, RelationshipType.LocalType, fragment, queue, visitedFiles, typeToFileIndex, workspaceRoot, filePath);
            }
        }

        // 8. typeof(T) and default(T)
        foreach (var to in container.DescendantNodes().OfType<TypeOfExpressionSyntax>())
        {
            string tName = GetSimpleTypeName(to.Type);
            ResolveAndLinkType(sourceNodeId, tName, RelationshipType.LocalType, fragment, queue, visitedFiles, typeToFileIndex, workspaceRoot, filePath);
        }

        foreach (var de in container.DescendantNodes().OfType<DefaultExpressionSyntax>())
        {
            string tName = GetSimpleTypeName(de.Type);
            ResolveAndLinkType(sourceNodeId, tName, RelationshipType.LocalType, fragment, queue, visitedFiles, typeToFileIndex, workspaceRoot, filePath);
        }

        // 9. Exception Catch Clauses (catch (CustomException ex))
        foreach (var catchDecl in container.DescendantNodes().OfType<CatchDeclarationSyntax>())
        {
            if (catchDecl.Type != null)
            {
                string exType = GetSimpleTypeName(catchDecl.Type);
                ResolveAndLinkType(sourceNodeId, exType, RelationshipType.ExceptionType, fragment, queue, visitedFiles, typeToFileIndex, workspaceRoot, filePath);
            }
        }

        // 10. General Identifier Safety-Net (Any reference to a known project type in expressions)
        var allIds = container.DescendantNodes().OfType<IdentifierNameSyntax>();
        foreach (var id in allIds)
        {
            if (id.Parent is BaseNamespaceDeclarationSyntax || id.Parent is NamespaceDeclarationSyntax || id.Parent is FileScopedNamespaceDeclarationSyntax)
                continue;

            // Skip member names being accessed on an instance (e.g. 'opt.Name' where Name is property)
            // UNLESS the expression is a known workspace type (like CommonValues in CommonValues.Fan_Name)
            if (id.Parent is MemberAccessExpressionSyntax ma && ma.Name == id)
            {
                continue;
            }

            string idText = id.Identifier.Text;
            if (typeToFileIndex.ContainsKey(idText))
            {
                ResolveAndLinkType(sourceNodeId, idText, RelationshipType.FieldType, fragment, queue, visitedFiles, typeToFileIndex, workspaceRoot, filePath);
            }
        }

        // 11. Extension Method Invocations (e.g. customer.ToDto(), builder.Services.AddMyServices(), str?.ToSlug())
        var invocations = container.DescendantNodes().OfType<InvocationExpressionSyntax>();
        foreach (var inv in invocations)
        {
            string? invokedMethodName = null;
            if (inv.Expression is MemberAccessExpressionSyntax ma)
            {
                invokedMethodName = (ma.Name is GenericNameSyntax gn) ? gn.Identifier.Text : ma.Name.Identifier.Text;
            }
            else if (inv.Expression is MemberBindingExpressionSyntax mb)
            {
                invokedMethodName = (mb.Name is GenericNameSyntax gn) ? gn.Identifier.Text : mb.Name.Identifier.Text;
            }

            if (!string.IsNullOrEmpty(invokedMethodName) && extensionMethodIndex.TryGetValue(invokedMethodName, out var extList))
            {
                foreach (var ext in extList)
                {
                    ResolveAndLinkExtensionMethod(sourceNodeId, ext, fragment, queue, visitedFiles, workspaceRoot, filePath);
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
        string workspaceRoot,
        string currentFilePath)
    {
        if (IsBuiltInSystemType(typeName)) return;

        if (typeToFileIndex.TryGetValue(typeName, out var targetFiles))
        {
            foreach (var targetFile in targetFiles)
            {
                if (string.Equals(targetFile, currentFilePath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string targetRel = Path.GetRelativePath(workspaceRoot, targetFile).Replace('\\', '/');
                string targetNodeId = $"file:{targetRel}";

                if (!fragment.Edges.Any(e => e.SourceNodeId == sourceNodeId && e.TargetNodeId == targetNodeId && e.Relationship == relationship))
                {
                    fragment.Edges.Add(new GraphEdge
                    {
                        SourceNodeId = sourceNodeId,
                        TargetNodeId = targetNodeId,
                        Relationship = relationship,
                        Confidence = Confidence.Verified,
                        AnalysisLevel = CapabilityLevel.Semantic
                    });
                }

                if (visitedFiles.Add(targetFile))
                {
                    queue.Enqueue(targetFile);
                }
            }
        }
    }

    private void ResolveAndLinkExtensionMethod(
        string sourceNodeId,
        ExtensionMethodInfo ext,
        DependencyGraphFragment fragment,
        Queue<string> queue,
        HashSet<string> visitedFiles,
        string workspaceRoot,
        string currentFilePath)
    {
        if (string.Equals(ext.DeclaringFilePath, currentFilePath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string targetRel = Path.GetRelativePath(workspaceRoot, ext.DeclaringFilePath).Replace('\\', '/');
        string targetNodeId = $"file:{targetRel}";

        if (!fragment.Edges.Any(e => e.SourceNodeId == sourceNodeId && e.TargetNodeId == targetNodeId && e.Relationship == RelationshipType.ExtensionMethod))
        {
            fragment.Edges.Add(new GraphEdge
            {
                SourceNodeId = sourceNodeId,
                TargetNodeId = targetNodeId,
                Relationship = RelationshipType.ExtensionMethod,
                Confidence = Confidence.Verified,
                AnalysisLevel = CapabilityLevel.Semantic
            });
        }

        if (visitedFiles.Add(ext.DeclaringFilePath))
        {
            queue.Enqueue(ext.DeclaringFilePath);
        }
    }

    private IEnumerable<string> ExtractTargetTypeNames(ExpressionSyntax? expr)
    {
        if (expr == null) yield break;

        if (expr is IdentifierNameSyntax id)
        {
            yield return id.Identifier.Text;
        }
        else if (expr is MemberAccessExpressionSyntax ma)
        {
            yield return ma.Name.Identifier.Text;
            foreach (var sub in ExtractTargetTypeNames(ma.Expression))
            {
                yield return sub;
            }
        }
        else if (expr is QualifiedNameSyntax q)
        {
            yield return q.Right.Identifier.Text;
            yield return q.Left.ToString().Split('.').Last();
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
                        @"\b(?:class|interface|struct|enum|record(?:\s+(?:class|struct))?)\s+([A-Za-z0-9_]+)");

                    foreach (System.Text.RegularExpressions.Match m in matches)
                    {
                        string name = m.Groups[1].Value;
                        if (!index.TryGetValue(name, out var list))
                        {
                            list = new List<string>();
                            index[name] = list;
                        }
                        if (!list.Contains(file))
                        {
                            list.Add(file);
                        }
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
                if (!list.Contains(file))
                {
                    list.Add(file);
                }
            }
        }
        return index;
    }

    private Dictionary<string, List<ExtensionMethodInfo>> BuildExtensionMethodIndex(List<string> files, string workspaceRoot)
    {
        var index = new Dictionary<string, List<ExtensionMethodInfo>>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            if (!file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) continue;

            try
            {
                string text = File.ReadAllText(file);
                // Fast pre-filter: must contain "static" and "this" to declare an extension method
                if (!text.Contains("static") || !text.Contains("this")) continue;

                var tree = CSharpSyntaxTree.ParseText(text, path: file);
                var root = tree.GetCompilationUnitRoot();

                string ns = root.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault()?.Name.ToString() ?? "";

                var classDecls = root.DescendantNodes().OfType<ClassDeclarationSyntax>()
                    .Where(c => c.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword)));

                foreach (var cls in classDecls)
                {
                    string className = cls.Identifier.Text;
                    var methods = cls.DescendantNodes().OfType<MethodDeclarationSyntax>()
                        .Where(m => m.Modifiers.Any(mod => mod.IsKind(SyntaxKind.StaticKeyword)));

                    foreach (var method in methods)
                    {
                        if (method.ParameterList.Parameters.Count > 0)
                        {
                            var firstParam = method.ParameterList.Parameters[0];
                            if (firstParam.Modifiers.Any(m => m.IsKind(SyntaxKind.ThisKeyword)) && firstParam.Type != null)
                            {
                                string methodName = method.Identifier.Text;
                                string targetTypeName = GetSimpleTypeName(firstParam.Type);

                                var info = new ExtensionMethodInfo
                                {
                                    MethodName = methodName,
                                    DeclaringClassName = className,
                                    DeclaringFilePath = file,
                                    TargetTypeName = targetTypeName,
                                    Namespace = ns
                                };

                                if (!index.TryGetValue(methodName, out var list))
                                {
                                    list = new List<ExtensionMethodInfo>();
                                    index[methodName] = list;
                                }
                                list.Add(info);
                            }
                        }
                    }
                }
            }
            catch { }
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
                       "float" or "decimal" or "char" or "byte" or "sbyte" or "short" or
                       "ushort" or "uint" or "ulong" or "nint" or "nuint" or "object" or
                       "Task" or "ValueTask" or "IEnumerable" or "List" or "Dictionary" or
                       "HashSet" or "Action" or "Func" or "IDisposable" or "IAsyncDisposable" or
                       "DateTime" or "DateTimeOffset" or "TimeSpan" or "DateOnly" or "TimeOnly" or
                       "Guid" or "CancellationToken" or "var" or "dynamic";
    }
}
