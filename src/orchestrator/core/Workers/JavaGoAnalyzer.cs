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

            // Discovered package & imports
            var importMatches = Regex.Matches(content, @"^\s*import\s+(?:static\s+)?([A-Za-z0-9_.]+);", RegexOptions.Multiline);
            foreach (Match m in importMatches)
            {
                string importedType = m.Groups[1].Value;
                string simpleName = importedType.Split('.').Last();

                // Search for local matching java file in workspace
                var localFiles = Directory.GetFiles(workspaceRoot, $"{simpleName}.java", SearchOption.AllDirectories);
                foreach (var lf in localFiles)
                {
                    string targetRel = Path.GetRelativePath(workspaceRoot, lf).Replace('\\', '/');
                    fragment.Edges.Add(new GraphEdge
                    {
                        SourceNodeId = fileNodeId,
                        TargetNodeId = $"file:{targetRel}",
                        Relationship = RelationshipType.Import,
                        Confidence = Confidence.High,
                        AnalysisLevel = CapabilityLevel.SyntaxAware
                    });

                    if (visited.Add(lf)) queue.Enqueue(lf);
                }
            }

            // Sibling types in same package directory and type references
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
                        fragment.Edges.Add(new GraphEdge
                        {
                            SourceNodeId = fileNodeId,
                            TargetNodeId = $"file:{targetRel}",
                            Relationship = RelationshipType.Interface,
                            Confidence = Confidence.High,
                            AnalysisLevel = CapabilityLevel.SyntaxAware
                        });
                    }

                    if (visited.Add(sjf)) queue.Enqueue(sjf);
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
