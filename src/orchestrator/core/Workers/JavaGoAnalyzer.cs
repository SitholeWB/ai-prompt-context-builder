using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using AiPromptContextBuilder.Core.Protocol;

namespace AiPromptContextBuilder.Core.Workers;

public static class JavaGoAnalyzer
{
    public static DependencyGraphFragment AnalyzeJava(string rootPath, string workspaceRoot)
    {
        var fragment = new DependencyGraphFragment();
        string normalizedRoot = Path.GetFullPath(rootPath);
        if (!File.Exists(normalizedRoot)) return fragment;

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>();
        queue.Enqueue(normalizedRoot);
        visited.Add(normalizedRoot);

        while (queue.Count > 0)
        {
            string curFile = queue.Dequeue();
            string rel = Path.GetRelativePath(workspaceRoot, curFile).Replace('\\', '/');
            string content = File.ReadAllText(curFile);
            string fileNodeId = $"file:{rel}";

            fragment.Nodes.Add(new GraphNode
            {
                Id = fileNodeId,
                Kind = NodeKind.File,
                Language = "Java",
                AnalysisLevel = CapabilityLevel.SyntaxAware,
                DisplayName = Path.GetFileName(curFile),
                QualifiedName = rel,
                RelativePath = rel,
                SourceAvailable = true,
                Content = content
            });

            // 1. Discovered package & imports (handling both regular and static imports)
            var importMatches = Regex.Matches(content, @"^\s*import\s+(static\s+)?([A-Za-z0-9_.]+);", RegexOptions.Multiline);
            foreach (Match m in importMatches)
            {
                bool isStatic = m.Groups[1].Success;
                string importedType = m.Groups[2].Value;
                var parts = importedType.Split('.');

                // Search right to left for the declaring class
                for (int i = parts.Length - 1; i >= 0; i--)
                {
                    string candidateName = parts[i];
                    if (string.IsNullOrEmpty(candidateName) || candidateName == "*") continue;

                    var localFiles = Directory.GetFiles(workspaceRoot, $"{candidateName}.java", SearchOption.AllDirectories);
                    if (localFiles.Length > 0)
                    {
                        var relType = isStatic ? RelationshipType.ExtensionMethod : RelationshipType.Import;
                        foreach (var lf in localFiles)
                        {
                            string targetRel = Path.GetRelativePath(workspaceRoot, lf).Replace('\\', '/');
                            if (!fragment.Edges.Any(e => e.SourceNodeId == fileNodeId && e.TargetNodeId == $"file:{targetRel}"))
                            {
                                fragment.Edges.Add(new GraphEdge
                                {
                                    SourceNodeId = fileNodeId,
                                    TargetNodeId = $"file:{targetRel}",
                                    Relationship = relType,
                                    Confidence = Confidence.High,
                                    AnalysisLevel = CapabilityLevel.SyntaxAware
                                });
                            }

                            if (visited.Add(lf)) queue.Enqueue(lf);
                        }
                        break;
                    }
                }
            }

            // 2. Lombok @ExtensionMethod({CustomerExtensions.class, ...})
            var extMatches = Regex.Matches(content, @"@ExtensionMethod\s*\(\s*\{?([^)]+)\}?\s*\)");
            foreach (Match em in extMatches)
            {
                var classRefs = Regex.Matches(em.Groups[1].Value, @"([A-Za-z0-9_]+)\.class");
                foreach (Match cr in classRefs)
                {
                    string className = cr.Groups[1].Value;
                    var localFiles = Directory.GetFiles(workspaceRoot, $"{className}.java", SearchOption.AllDirectories);
                    foreach (var lf in localFiles)
                    {
                        string targetRel = Path.GetRelativePath(workspaceRoot, lf).Replace('\\', '/');
                        if (!fragment.Edges.Any(e => e.SourceNodeId == fileNodeId && e.TargetNodeId == $"file:{targetRel}"))
                        {
                            fragment.Edges.Add(new GraphEdge
                            {
                                SourceNodeId = fileNodeId,
                                TargetNodeId = $"file:{targetRel}",
                                Relationship = RelationshipType.ExtensionMethod,
                                Confidence = Confidence.Verified,
                                AnalysisLevel = CapabilityLevel.SyntaxAware
                            });
                        }

                        if (visited.Add(lf)) queue.Enqueue(lf);
                    }
                }
            }

            // 3. Static helper & utility method calls (e.g. CustomerExtensions.toDto(...), StringUtils.isBlank(...))
            var staticCalls = Regex.Matches(content, @"\b([A-Z][A-Za-z0-9_]+)\.[a-z][A-Za-z0-9_]*\s*\(");
            foreach (Match sc in staticCalls)
            {
                string className = sc.Groups[1].Value;
                var localFiles = Directory.GetFiles(workspaceRoot, $"{className}.java", SearchOption.AllDirectories);
                foreach (var lf in localFiles)
                {
                    string targetRel = Path.GetRelativePath(workspaceRoot, lf).Replace('\\', '/');
                    if (!fragment.Edges.Any(e => e.SourceNodeId == fileNodeId && e.TargetNodeId == $"file:{targetRel}"))
                    {
                        var relType = className.EndsWith("Extensions") || className.EndsWith("Extension") || className.EndsWith("Utils") || className.EndsWith("Helper")
                            ? RelationshipType.ExtensionMethod
                            : RelationshipType.MethodCall;

                        fragment.Edges.Add(new GraphEdge
                        {
                            SourceNodeId = fileNodeId,
                            TargetNodeId = $"file:{targetRel}",
                            Relationship = relType,
                            Confidence = Confidence.High,
                            AnalysisLevel = CapabilityLevel.SyntaxAware
                        });
                    }

                    if (visited.Add(lf)) queue.Enqueue(lf);
                }
            }

            // 4. Sibling types in same package directory and type references
            string dir = Path.GetDirectoryName(curFile) ?? workspaceRoot;
            var siblingJavaFiles = Directory.GetFiles(dir, "*.java");
            foreach (var sjf in siblingJavaFiles)
            {
                if (string.Equals(sjf, curFile, StringComparison.OrdinalIgnoreCase)) continue;

                string typeName = Path.GetFileNameWithoutExtension(sjf);
                // Check if the type name is referenced in the content
                if (Regex.IsMatch(content, $@"\b{Regex.Escape(typeName)}\b"))
                {
                    string targetRel = Path.GetRelativePath(workspaceRoot, sjf).Replace('\\', '/');
                    if (!fragment.Edges.Any(e => e.SourceNodeId == fileNodeId && e.TargetNodeId == $"file:{targetRel}"))
                    {
                        var relType = typeName.EndsWith("Extensions") || typeName.EndsWith("Extension")
                            ? RelationshipType.ExtensionMethod
                            : RelationshipType.Interface;

                        fragment.Edges.Add(new GraphEdge
                        {
                            SourceNodeId = fileNodeId,
                            TargetNodeId = $"file:{targetRel}",
                            Relationship = relType,
                            Confidence = Confidence.High,
                            AnalysisLevel = CapabilityLevel.SyntaxAware
                        });
                    }

                    if (visited.Add(sjf)) queue.Enqueue(sjf);
                }
            }

            // 5. Interface implementations (Dependency Injection)
            var implementsMatches = Regex.Matches(content, @"\bimplements\s+([A-Za-z0-9_,\s]+)");
            foreach (Match im in implementsMatches)
            {
                var ifaces = im.Groups[1].Value.Split(',').Select(s => s.Trim());
                foreach (var iface in ifaces)
                {
                    if (string.IsNullOrEmpty(iface)) continue;
                    var ifaceFiles = Directory.GetFiles(workspaceRoot, $"{iface}.java", SearchOption.AllDirectories);
                    foreach (var f in ifaceFiles)
                    {
                        LinkJavaFile(fileNodeId, f, RelationshipType.Interface, fragment, queue, visited, workspaceRoot);
                    }
                }
            }

            // Look for *Impl.java files for referenced interfaces
            var referencedTypes = Regex.Matches(content, @"\b([A-Z][A-Za-z0-9_]+)\b");
            foreach (Match rt in referencedTypes)
            {
                string tName = rt.Groups[1].Value;
                var implFiles = Directory.GetFiles(workspaceRoot, $"{tName}Impl.java", SearchOption.AllDirectories);
                foreach (var f in implFiles)
                {
                    LinkJavaFile(fileNodeId, f, RelationshipType.Implementation, fragment, queue, visited, workspaceRoot);
                }
            }

            // 6. Spring Event Listeners & Event Subscribers (@EventListener, @KafkaListener, @RabbitListener)
            var eventListenerMatches = Regex.Matches(content, @"@(?:EventListener|TransactionalEventListener|KafkaListener|RabbitListener)(?:\s*\(\s*(?:value\s*=\s*)?\{?([A-Za-z0-9_]+)\.class)?");
            foreach (Match elm in eventListenerMatches)
            {
                if (elm.Groups[1].Success)
                {
                    string eventClass = elm.Groups[1].Value;
                    var eventFiles = Directory.GetFiles(workspaceRoot, $"{eventClass}.java", SearchOption.AllDirectories);
                    foreach (var ef in eventFiles)
                    {
                        LinkJavaFile(fileNodeId, ef, RelationshipType.EventType, fragment, queue, visited, workspaceRoot);
                    }
                }
            }

            var eventMethodMatches = Regex.Matches(content, @"\b(?:on|handle|consume|listen|process)\s*\(\s*([A-Za-z0-9_]+(?:Event|Command|Notification|Message))\b");
            foreach (Match emm in eventMethodMatches)
            {
                string eventClass = emm.Groups[1].Value;
                var eventFiles = Directory.GetFiles(workspaceRoot, $"{eventClass}.java", SearchOption.AllDirectories);
                foreach (var ef in eventFiles)
                {
                    LinkJavaFile(fileNodeId, ef, RelationshipType.EventType, fragment, queue, visited, workspaceRoot);
                }
            }

            // 7. Annotations (@Annotation or @Annotation(Target.class))
            var annotationMatches = Regex.Matches(content, @"@([A-Z][A-Za-z0-9_]+)(?:\s*\(\s*(?:value\s*=\s*)?\{?([A-Za-z0-9_]+)\.class)?");
            foreach (Match am in annotationMatches)
            {
                string annoName = am.Groups[1].Value;
                if (annoName is not ("Override" or "Deprecated" or "SuppressWarnings" or "Nullable" or "NonNull" or "Test"))
                {
                    var annoFiles = Directory.GetFiles(workspaceRoot, $"{annoName}.java", SearchOption.AllDirectories);
                    foreach (var af in annoFiles)
                    {
                        LinkJavaFile(fileNodeId, af, RelationshipType.Annotation, fragment, queue, visited, workspaceRoot);
                    }
                }
                if (am.Groups[2].Success)
                {
                    string targetClass = am.Groups[2].Value;
                    var targetFiles = Directory.GetFiles(workspaceRoot, $"{targetClass}.java", SearchOption.AllDirectories);
                    foreach (var tf in targetFiles)
                    {
                        LinkJavaFile(fileNodeId, tf, RelationshipType.Annotation, fragment, queue, visited, workspaceRoot);
                    }
                }
            }

            // Discovered classes/interfaces
            var typeMatches = Regex.Matches(content, @"\b(?:class|interface|record|enum)\s+([A-Za-z0-9_]+)");
            foreach (Match m in typeMatches)
            {
                string typeName = m.Groups[1].Value;
                fragment.Nodes.Add(new GraphNode
                {
                    Id = $"type:{rel}#{typeName}",
                    Kind = NodeKind.Class,
                    Language = "Java",
                    AnalysisLevel = CapabilityLevel.SyntaxAware,
                    DisplayName = typeName,
                    QualifiedName = typeName,
                    RelativePath = rel,
                    SourceAvailable = true
                });
            }
        }

        return fragment;
    }

    private static void LinkJavaFile(
        string sourceNodeId,
        string targetFile,
        RelationshipType relationship,
        DependencyGraphFragment fragment,
        Queue<string> queue,
        HashSet<string> visited,
        string workspaceRoot)
    {
        string targetRel = Path.GetRelativePath(workspaceRoot, targetFile).Replace('\\', '/');
        string targetNodeId = $"file:{targetRel}";
        if (!fragment.Edges.Any(e => e.SourceNodeId == sourceNodeId && e.TargetNodeId == targetNodeId && e.Relationship == relationship))
        {
            fragment.Edges.Add(new GraphEdge
            {
                SourceNodeId = sourceNodeId,
                TargetNodeId = targetNodeId,
                Relationship = relationship,
                Confidence = Confidence.High,
                AnalysisLevel = CapabilityLevel.SyntaxAware
            });
        }
        if (visited.Add(targetFile)) queue.Enqueue(targetFile);
    }

    public static DependencyGraphFragment AnalyzeGo(string rootPath, string workspaceRoot)
    {
        var fragment = new DependencyGraphFragment();
        string normalizedRoot = Path.GetFullPath(rootPath);
        if (!File.Exists(normalizedRoot)) return fragment;

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>();
        queue.Enqueue(normalizedRoot);
        visited.Add(normalizedRoot);

        while (queue.Count > 0)
        {
            string curFile = queue.Dequeue();
            string rel = Path.GetRelativePath(workspaceRoot, curFile).Replace('\\', '/');
            string content = File.ReadAllText(curFile);
            string fileNodeId = $"file:{rel}";

            fragment.Nodes.Add(new GraphNode
            {
                Id = fileNodeId,
                Kind = NodeKind.File,
                Language = "Go",
                AnalysisLevel = CapabilityLevel.SyntaxAware,
                DisplayName = Path.GetFileName(curFile),
                QualifiedName = rel,
                RelativePath = rel,
                SourceAvailable = true,
                Content = content
            });

            // Package declaration
            var pkgMatch = Regex.Match(content, @"^\s*package\s+([A-Za-z0-9_]+)", RegexOptions.Multiline);
            string currentPkg = pkgMatch.Success ? pkgMatch.Groups[1].Value : "main";

            // Sibling files in same package directory
            string dir = Path.GetDirectoryName(curFile) ?? workspaceRoot;
            var siblingGoFiles = Directory.GetFiles(dir, "*.go").Where(f => !f.EndsWith("_test.go")).ToList();
            foreach (var sf in siblingGoFiles)
            {
                if (!string.Equals(sf, curFile, StringComparison.OrdinalIgnoreCase))
                {
                    string targetRel = Path.GetRelativePath(workspaceRoot, sf).Replace('\\', '/');
                    fragment.Edges.Add(new GraphEdge
                    {
                        SourceNodeId = fileNodeId,
                        TargetNodeId = $"file:{targetRel}",
                        Relationship = RelationshipType.Import,
                        Confidence = Confidence.Verified,
                        AnalysisLevel = CapabilityLevel.SyntaxAware
                    });

                    if (visited.Add(sf)) queue.Enqueue(sf);
                }
            }

            // Local package and extension imports (e.g. import "myproject/extensions", import "./utils")
            var goImports = Regex.Matches(content, @"import\s*\(([\s\S]*?)\)|import\s+['""]([^'""]+)['""]");
            foreach (Match gi in goImports)
            {
                string importBlock = gi.Groups[1].Success ? gi.Groups[1].Value : gi.Groups[2].Value;
                var strMatches = Regex.Matches(importBlock, @"['""]([^'""]+)['""]");
                foreach (Match sm in strMatches)
                {
                    string imp = sm.Groups[1].Value;
                    string pkgDirName = imp.Split('/').Last();
                    if (string.IsNullOrEmpty(pkgDirName)) continue;

                    var dirs = Directory.GetDirectories(workspaceRoot, pkgDirName, SearchOption.AllDirectories);
                    foreach (var d in dirs)
                    {
                        var goFiles = Directory.GetFiles(d, "*.go").Where(f => !f.EndsWith("_test.go"));
                        foreach (var gf in goFiles)
                        {
                            string targetRel = Path.GetRelativePath(workspaceRoot, gf).Replace('\\', '/');
                            if (!fragment.Edges.Any(e => e.SourceNodeId == fileNodeId && e.TargetNodeId == $"file:{targetRel}"))
                            {
                                var relType = pkgDirName.Contains("ext", StringComparison.OrdinalIgnoreCase)
                                    ? RelationshipType.ExtensionMethod
                                    : RelationshipType.Import;

                                fragment.Edges.Add(new GraphEdge
                                {
                                    SourceNodeId = fileNodeId,
                                    TargetNodeId = $"file:{targetRel}",
                                    Relationship = relType,
                                    Confidence = Confidence.High,
                                    AnalysisLevel = CapabilityLevel.SyntaxAware
                                });
                                if (visited.Add(gf)) queue.Enqueue(gf);
                            }
                        }
                    }
                }
            }

            // Struct and interface declarations
            var structMatches = Regex.Matches(content, @"type\s+([A-Za-z0-9_]+)\s+(?:struct|interface)");
            foreach (Match m in structMatches)
            {
                string structName = m.Groups[1].Value;
                fragment.Nodes.Add(new GraphNode
                {
                    Id = $"type:{rel}#{structName}",
                    Kind = NodeKind.Struct,
                    Language = "Go",
                    AnalysisLevel = CapabilityLevel.SyntaxAware,
                    DisplayName = structName,
                    QualifiedName = $"{currentPkg}.{structName}",
                    RelativePath = rel,
                    SourceAvailable = true
                });
            }
        }

        return fragment;
    }
}
