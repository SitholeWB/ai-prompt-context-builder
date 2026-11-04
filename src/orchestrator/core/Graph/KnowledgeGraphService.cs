using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using AiPromptContextBuilder.Core.Markdown;
using AiPromptContextBuilder.Core.Protocol;
using AiPromptContextBuilder.Core.Security;
using AiPromptContextBuilder.Core.Tokenizer;
using AiPromptContextBuilder.Core.Workers;
using AiPromptContextBuilder.Core.Workspace;

namespace AiPromptContextBuilder.Core.Graph;

public class KnowledgeGraphNodeDto
{
    public string Id { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string Cluster { get; set; } = string.Empty;
    public string Role { get; set; } = "Module";
    public int LineCount { get; set; }
    public int RawTokens { get; set; }
    public int InDegree { get; set; }
    public int OutDegree { get; set; }
    public double PageRank { get; set; }
    public bool IsGodNode { get; set; }
    public bool IsInCycle { get; set; }
    public bool IsModified { get; set; }
    public bool IsOrphan { get; set; }
    public List<string> Symbols { get; set; } = new();
    public List<string> OutgoingTargets { get; set; } = new();
    public List<string> IncomingSources { get; set; } = new();
}

public class KnowledgeGraphEdgeDto
{
    public string Source { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string Relationship { get; set; } = "Import";
}

public class KnowledgeGraphClusterDto
{
    public string Name { get; set; } = string.Empty;
    public int FileCount { get; set; }
    public int TotalLines { get; set; }
    public List<string> Files { get; set; } = new();
}

public class KnowledgeGraphResult
{
    public bool Success { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public string WorkspaceName { get; set; } = string.Empty;
    public string WorkspaceRoot { get; set; } = string.Empty;
    public string ScopeDescription { get; set; } = "Entire Codebase";
    public string? OutputPath { get; set; }
    public int TotalFiles { get; set; }
    public int TotalEdges { get; set; }
    public int TotalSymbols { get; set; }
    public int TotalLines { get; set; }
    public int RawCodebaseTokens { get; set; }
    public int GraphMarkdownTokens { get; set; }
    public double TokenSavingsPercent { get; set; }
    public List<string> Languages { get; set; } = new();
    public List<KnowledgeGraphNodeDto> Nodes { get; set; } = new();
    public List<KnowledgeGraphEdgeDto> Edges { get; set; } = new();
    public List<KnowledgeGraphNodeDto> GodNodes { get; set; } = new();
    public List<List<string>> CircularCycles { get; set; } = new();
    public List<KnowledgeGraphClusterDto> Clusters { get; set; } = new();
    public List<string> OrphanFiles { get; set; } = new();
    public string MermaidDiagram { get; set; } = string.Empty;
    public string GeneratedMarkdown { get; set; } = string.Empty;
    public bool IncludesSourceCode { get; set; }
    public string? FocusedFile { get; set; }
}

public static class KnowledgeGraphService
{
    private static readonly HashSet<string> SupportedSourceExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".vb", ".ts", ".tsx", ".js", ".jsx", ".mjs", ".cjs", ".vue",
        ".py", ".java", ".go", ".rs", ".kt", ".kts", ".php", ".dart",
        ".cpp", ".cc", ".cxx", ".c", ".h", ".hpp", ".hxx",
        ".razor", ".cshtml", ".html", ".htm", ".css", ".scss", ".sass"
    };

    private static readonly Regex TypeDeclRegex = new(
        @"^\s*(?:public|private|protected|internal|export|default|abstract|sealed|static|partial|open|data|\s)*\s*(?:class|interface|struct|enum|record|trait|type)\s+([A-Za-z_][A-Za-z0-9_]*)",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex FuncDeclRegex = new(
        @"(?:^\s*(?:public|private|protected|internal|export|async|static|virtual|override|fn|func|def|function)\s+(?:[A-Za-z0-9_<>,\.\[\]\?\s]+\s+)?([A-Za-z_][A-Za-z0-9_]*)\s*\()|(?:^\s*(?:export\s+)?(?:const|let)\s+([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?:async\s*)?(?:\([^)]*\)|[A-Za-z_][A-Za-z0-9_]*)\s*=>)",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex CSharpInheritanceRegex = new(
        @"(?:class|struct|record|interface)\s+([A-Za-z_][A-Za-z0-9_]*)(?:<[^>]+>)?\s*:\s*([^\r\n\{]+)",
        RegexOptions.Compiled);

    public static KnowledgeGraphResult BuildKnowledgeGraph(
        string workspaceOrTargetPath,
        bool includeTests = false,
        GitScopeMode gitScope = GitScopeMode.None,
        string? gitCompareTarget = null,
        int? gitCommitCount = null,
        bool saveMarkdown = true,
        string? customOutputPath = null,
        string outputLanguage = "en",
        bool includeSourceCode = false,
        int? maxTokens = null,
        bool filterToTargetFile = false,
        Action<int, string>? progressCallback = null,
        string? targetFilePath = null)
    {
        try
        {
            progressCallback?.Invoke(5, "Discovering workspace & indexing source files...");

            string? targetFile = !string.IsNullOrWhiteSpace(targetFilePath) && File.Exists(targetFilePath)
                ? Path.GetFullPath(targetFilePath)
                : (File.Exists(workspaceOrTargetPath) ? Path.GetFullPath(workspaceOrTargetPath) : null);

            string fullTarget = targetFile ?? Path.GetFullPath(workspaceOrTargetPath);
            var discovered = WorkspaceDiscovery.Discover(fullTarget, Directory.Exists(fullTarget) ? fullTarget : (Directory.Exists(workspaceOrTargetPath) ? Path.GetFullPath(workspaceOrTargetPath) : null));
            string wsRoot = Directory.Exists(fullTarget) ? fullTarget : (Directory.Exists(workspaceOrTargetPath) ? Path.GetFullPath(workspaceOrTargetPath) : discovered.WorkspaceRoot);
            string wsName = Path.GetFileName(wsRoot.TrimEnd('/', '\\'));
            if (string.IsNullOrEmpty(wsName)) wsName = "Workspace";

            var wsIndex = new WorkspaceFileIndex(wsRoot);
            var estimator = new ApproximateTokenEstimator();

            // Optional Git modified highlight set
            var modifiedRelPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string scopeDesc = "Entire Codebase";

            if (gitScope != GitScopeMode.None)
            {
                var gitRes = GitDiffService.ResolveGitChanges(wsRoot, gitScope, targetFile, gitCompareTarget, gitCommitCount);
                scopeDesc = $"Codebase Knowledge Graph ({gitRes.ScopeDescription})";
                foreach (var absMod in gitRes.ModifiedAbsolutePaths)
                {
                    string relMod = Path.GetRelativePath(wsRoot, absMod).Replace('\\', '/');
                    modifiedRelPaths.Add(relMod);
                }
            }
            else if (targetFile != null)
            {
                string relFocus = Path.GetRelativePath(wsRoot, targetFile).Replace('\\', '/');
                modifiedRelPaths.Add(relFocus);
                scopeDesc = $"Codebase Knowledge Graph (Focused on {relFocus})";
            }

            // 1. Collect eligible source files in workspace
            var sourceFiles = new List<string>();
            foreach (var rel in wsIndex.AllRelativeFiles)
            {
                string ext = Path.GetExtension(rel);
                if (!SupportedSourceExtensions.Contains(ext)) continue;

                // Skip test fixtures or test files unless includeTests is true
                if (!includeTests && IsTestOrFixturePath(rel)) continue;

                // Skip generated files
                if (rel.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase) ||
                    rel.EndsWith(".designer.cs", StringComparison.OrdinalIgnoreCase) ||
                    rel.EndsWith(".d.ts", StringComparison.OrdinalIgnoreCase) ||
                    rel.EndsWith(".min.js", StringComparison.OrdinalIgnoreCase) ||
                    rel.EndsWith(".min.css", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                sourceFiles.Add(rel);
            }

            // If a workspace only has files inside tests/fixtures and nothing else, include all source files
            if (sourceFiles.Count == 0)
            {
                foreach (var rel in wsIndex.AllRelativeFiles)
                {
                    if (SupportedSourceExtensions.Contains(Path.GetExtension(rel)))
                    {
                        sourceFiles.Add(rel);
                    }
                }
            }

            if (sourceFiles.Count == 0)
            {
                return new KnowledgeGraphResult
                {
                    Success = false,
                    ErrorCode = "NoSourceFiles",
                    ErrorMessage = $"No supported source files found in '{wsRoot}'."
                };
            }

            sourceFiles.Sort(StringComparer.OrdinalIgnoreCase);

            progressCallback?.Invoke(20, $"Extracting AST symbols across {sourceFiles.Count} files...");

            var fileContents = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var nodeMap = new Dictionary<string, KnowledgeGraphNodeDto>(StringComparer.OrdinalIgnoreCase);
            var typeToFileMap = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var languagesSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int totalRawTokens = 0;
            int totalLines = 0;
            int totalSymbols = 0;

            // Pass 1: Read files, extract declared types & functions, and build symbol lookup table
            for (int i = 0; i < sourceFiles.Count; i++)
            {
                string rel = sourceFiles[i];
                string abs = Path.Combine(wsRoot, rel);
                string content = "";
                try { content = File.ReadAllText(abs); } catch { continue; }

                fileContents[rel] = content;
                string ext = Path.GetExtension(rel).ToLowerInvariant();
                string lang = MapExtensionToLanguage(ext);
                languagesSet.Add(lang);

                int lines = content.Split('\n').Length;
                int tokens = estimator.EstimateTokens(content).TokenCount;
                totalLines += lines;
                totalRawTokens += tokens;

                var declaredTypes = ExtractDeclaredTypes(content);
                var declaredFuncs = ExtractDeclaredFunctions(content, declaredTypes);

                foreach (var t in declaredTypes)
                {
                    if (!typeToFileMap.TryGetValue(t, out var list))
                    {
                        list = new List<string>();
                        typeToFileMap[t] = list;
                    }
                    if (!list.Contains(rel, StringComparer.OrdinalIgnoreCase))
                    {
                        list.Add(rel);
                    }
                }

                // Also map file stem for module/component resolution
                string stem = Path.GetFileNameWithoutExtension(rel);
                if (stem.Length > 2 && !stem.Equals("Program", StringComparison.OrdinalIgnoreCase) && !stem.Equals("index", StringComparison.OrdinalIgnoreCase))
                {
                    if (!typeToFileMap.TryGetValue(stem, out var stemList))
                    {
                        stemList = new List<string>();
                        typeToFileMap[stem] = stemList;
                    }
                    if (!stemList.Contains(rel, StringComparer.OrdinalIgnoreCase))
                    {
                        stemList.Add(rel);
                    }
                }

                var combinedSymbols = declaredTypes
                    .Concat(declaredFuncs.Select(f => $"{f}()"))
                    .Distinct(StringComparer.Ordinal)
                    .Take(12)
                    .ToList();

                totalSymbols += combinedSymbols.Count;

                nodeMap[rel] = new KnowledgeGraphNodeDto
                {
                    Id = rel,
                    RelativePath = rel,
                    FileName = Path.GetFileName(rel),
                    Language = lang,
                    Cluster = DetermineClusterName(rel),
                    LineCount = lines,
                    RawTokens = tokens,
                    IsModified = modifiedRelPaths.Contains(rel),
                    Symbols = combinedSymbols
                };
            }

            progressCallback?.Invoke(50, "Resolving cross-file AST dependencies, interfaces & imports...");

            // Pass 2: Build Directed Edges across the entire codebase
            var edgeSet = new Dictionary<(string Source, string Target), string>();

            void RecordEdge(string srcRel, string tgtRel, string relKind)
            {
                if (string.IsNullOrEmpty(srcRel) || string.IsNullOrEmpty(tgtRel)) return;
                srcRel = srcRel.Replace('\\', '/');
                tgtRel = tgtRel.Replace('\\', '/');
                if (srcRel.Equals(tgtRel, StringComparison.OrdinalIgnoreCase)) return;
                if (!nodeMap.ContainsKey(srcRel) || !nodeMap.ContainsKey(tgtRel)) return;

                var key = (srcRel, tgtRel);
                if (!edgeSet.ContainsKey(key))
                {
                    edgeSet[key] = relKind;
                }
            }

            foreach (var rel in sourceFiles)
            {
                if (!fileContents.TryGetValue(rel, out var content)) continue;
                string abs = Path.Combine(wsRoot, rel);
                string ext = Path.GetExtension(rel).ToLowerInvariant();

                // 2a. Language-specific import/module analyzers
                DependencyGraphFragment? frag = ext switch
                {
                    ".ts" or ".tsx" or ".js" or ".jsx" or ".mjs" or ".cjs" or ".vue" or ".html" or ".css" or ".scss" or ".sass"
                        => BuiltInSyntaxAnalyzer.AnalyzeJsTs(abs, wsRoot),
                    ".py" => BuiltInSyntaxAnalyzer.AnalyzePython(abs, wsRoot),
                    ".java" => JavaGoAnalyzer.AnalyzeJava(abs, wsRoot),
                    ".go" => JavaGoAnalyzer.AnalyzeGo(abs, wsRoot),
                    ".rs" => ExtendedLanguageAnalyzers.AnalyzeRust(abs, wsRoot),
                    ".kt" or ".kts" => ExtendedLanguageAnalyzers.AnalyzeKotlin(abs, wsRoot),
                    ".php" => ExtendedLanguageAnalyzers.AnalyzePhp(abs, wsRoot),
                    ".dart" => ExtendedLanguageAnalyzers.AnalyzeDart(abs, wsRoot),
                    ".cpp" or ".cc" or ".cxx" or ".c" or ".h" or ".hpp" or ".hxx"
                        => ExtendedLanguageAnalyzers.AnalyzeCpp(abs, wsRoot),
                    _ => null
                };

                if (frag != null)
                {
                    foreach (var edge in frag.Edges)
                    {
                        string s = StripFilePrefix(edge.SourceNodeId);
                        string t = StripFilePrefix(edge.TargetNodeId);
                        RecordEdge(s, t, edge.Relationship.ToString());
                    }
                }

                // 2b. Cross-reference declared types & interfaces across files (handles C#, VB, and cross-file type references)
                ResolveTypeReferencesInContent(rel, content, typeToFileMap, RecordEdge);
            }

            progressCallback?.Invoke(75, "Running PageRank centrality & Tarjan circular dependency detection...");

            // Build adjacency lists on nodes
            var edgesList = new List<KnowledgeGraphEdgeDto>();
            foreach (var kv in edgeSet)
            {
                string src = kv.Key.Source;
                string tgt = kv.Key.Target;
                string relKind = kv.Value;

                edgesList.Add(new KnowledgeGraphEdgeDto
                {
                    Source = src,
                    Target = tgt,
                    Relationship = relKind
                });

                nodeMap[src].OutgoingTargets.Add(tgt);
                nodeMap[tgt].IncomingSources.Add(src);
            }

            foreach (var n in nodeMap.Values)
            {
                n.OutgoingTargets.Sort(StringComparer.OrdinalIgnoreCase);
                n.IncomingSources.Sort(StringComparer.OrdinalIgnoreCase);
                n.OutDegree = n.OutgoingTargets.Count;
                n.InDegree = n.IncomingSources.Count;
                n.IsOrphan = n.InDegree == 0 && n.OutDegree == 0;
            }

            // 3. Compute PageRank Centrality (measures architectural importance / "God Nodes")
            ComputePageRank(nodeMap);

            // 4. Run Tarjan's Strongly Connected Components (SCC) to detect Circular Dependency Cycles
            var cycles = FindCircularDependencyCycles(nodeMap);
            var nodesInCycles = new HashSet<string>(cycles.SelectMany(c => c), StringComparer.OrdinalIgnoreCase);
            foreach (var cNode in nodesInCycles)
            {
                if (nodeMap.TryGetValue(cNode, out var dto))
                {
                    dto.IsInCycle = true;
                }
            }

            // Optional single file subgraph filter: include the target file and all its transitive dependencies & callers
            if (filterToTargetFile && targetFile != null)
            {
                string relFocus = Path.GetRelativePath(wsRoot, targetFile).Replace('\\', '/');
                var reachable = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { relFocus };

                // Forward (Dependencies): nodes reachable from target
                var queue = new Queue<string>();
                queue.Enqueue(relFocus);
                while (queue.Count > 0)
                {
                    string curr = queue.Dequeue();
                    if (nodeMap.TryGetValue(curr, out var currNode))
                    {
                        foreach (var target in currNode.OutgoingTargets)
                        {
                            if (reachable.Add(target)) queue.Enqueue(target);
                        }
                    }
                }

                // Backward (Callers): nodes that reach target
                queue.Enqueue(relFocus);
                while (queue.Count > 0)
                {
                    string curr = queue.Dequeue();
                    if (nodeMap.TryGetValue(curr, out var currNode))
                    {
                        foreach (var src in currNode.IncomingSources)
                        {
                            if (reachable.Add(src)) queue.Enqueue(src);
                        }
                    }
                }

                // Filter nodes and edges strictly to this connected subgraph
                var filteredNodes = nodeMap.Values.Where(n => reachable.Contains(n.Id)).ToList();
                edgesList = edgesList.Where(e => reachable.Contains(e.Source) && reachable.Contains(e.Target)).ToList();
                scopeDesc = $"Single File Subgraph ({Path.GetFileName(relFocus)}: {filteredNodes.Count} connected files, {edgesList.Count} edges)";

                // Replace nodeMap with filtered nodes
                nodeMap.Clear();
                foreach (var fn in filteredNodes)
                {
                    nodeMap[fn.Id] = fn;
                }
            }

            // 5. Identify Top "God Nodes" (Core Architectural Hubs) & Classify Node Roles
            var allNodesSorted = nodeMap.Values
                .OrderByDescending(n => n.PageRank)
                .ThenByDescending(n => n.InDegree + n.OutDegree)
                .ThenBy(n => n.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ToList();

            int godNodeCount = Math.Min(Math.Max(3, allNodesSorted.Count / 6), 10);
            var godNodes = allNodesSorted
                .Where(n => n.InDegree >= 2 || (n.InDegree + n.OutDegree) >= 3)
                .Take(godNodeCount)
                .ToList();

            foreach (var gn in godNodes)
            {
                gn.IsGodNode = true;
            }

            foreach (var n in allNodesSorted)
            {
                n.Role = ClassifyNodeRole(n);
            }

            // 6. Group into Domain Clusters
            var clusters = allNodesSorted
                .GroupBy(n => n.Cluster, StringComparer.OrdinalIgnoreCase)
                .Select(g => new KnowledgeGraphClusterDto
                {
                    Name = g.Key,
                    FileCount = g.Count(),
                    TotalLines = g.Sum(x => x.LineCount),
                    Files = g.Select(x => x.RelativePath).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList()
                })
                .OrderByDescending(c => c.FileCount)
                .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var orphans = allNodesSorted
                .Where(n => n.IsOrphan)
                .Select(n => n.RelativePath)
                .ToList();

            progressCallback?.Invoke(90, "Generating Mermaid architecture flowchart & AI Knowledge Map...");

            // 7. Build Mermaid Diagram & AI Knowledge Map Markdown
            string mermaid = BuildMermaidArchitectureDiagram(allNodesSorted, edgesList);

            var result = new KnowledgeGraphResult
            {
                Success = true,
                WorkspaceName = wsName,
                WorkspaceRoot = wsRoot,
                ScopeDescription = scopeDesc,
                TotalFiles = allNodesSorted.Count,
                TotalEdges = edgesList.Count,
                TotalSymbols = totalSymbols,
                TotalLines = totalLines,
                RawCodebaseTokens = totalRawTokens,
                Languages = languagesSet.OrderBy(l => l, StringComparer.OrdinalIgnoreCase).ToList(),
                Nodes = allNodesSorted,
                Edges = edgesList,
                GodNodes = godNodes,
                CircularCycles = cycles,
                Clusters = clusters,
                OrphanFiles = orphans,
                MermaidDiagram = mermaid,
                IncludesSourceCode = includeSourceCode,
                FocusedFile = targetFile != null ? Path.GetRelativePath(wsRoot, targetFile).Replace('\\', '/') : null,
            };

            string markdown = RenderKnowledgeGraphMarkdown(result, outputLanguage, includeSourceCode, maxTokens, wsRoot);
            result.GeneratedMarkdown = markdown;
            result.GraphMarkdownTokens = estimator.EstimateTokens(markdown).TokenCount;
            result.TokenSavingsPercent = totalRawTokens > 0
                ? Math.Round(Math.Max(0.0, (1.0 - (result.GraphMarkdownTokens / (double)totalRawTokens)) * 100.0), 1)
                : 0.0;

            if (saveMarkdown)
            {
                string outFile = !string.IsNullOrWhiteSpace(customOutputPath)
                    ? customOutputPath
                    : "codebase-knowledge-graph.md";

                string finalPath = Path.IsPathRooted(outFile) ? outFile : Path.Combine(wsRoot, outFile);
                string? dir = Path.GetDirectoryName(finalPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                File.WriteAllText(finalPath, markdown);
                result.OutputPath = finalPath;
            }

            progressCallback?.Invoke(100, "Codebase Knowledge Graph built successfully!");
            return result;
        }
        catch (Exception ex)
        {
            return new KnowledgeGraphResult
            {
                Success = false,
                ErrorCode = "UnexpectedError",
                ErrorMessage = ex.Message
            };
        }
    }

    private static void ResolveTypeReferencesInContent(
        string srcRel,
        string content,
        Dictionary<string, List<string>> typeToFileMap,
        Action<string, string, string> recordEdge)
    {
        // Check explicit C# / TS / Java / Kotlin inheritance & interface implementation first
        foreach (Match m in CSharpInheritanceRegex.Matches(content))
        {
            string baseList = m.Groups[2].Value;
            foreach (Match tokenMatch in Regex.Matches(baseList, @"\b([A-Za-z_][A-Za-z0-9_]*)\b"))
            {
                string baseType = tokenMatch.Groups[1].Value;
                if (typeToFileMap.TryGetValue(baseType, out var targetFiles))
                {
                    string relKind = baseType.StartsWith("I") && baseType.Length > 1 && char.IsUpper(baseType[1])
                        ? "Interface"
                        : "BaseType";
                    foreach (var tf in targetFiles)
                    {
                        recordEdge(srcRel, tf, relKind);
                    }
                }
            }
        }

        // Tokenize all PascalCase / identifier tokens in the file and match against known workspace types in O(1) per token
        var seenTypes = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match idMatch in Regex.Matches(content, @"\b([A-Z][A-Za-z0-9_]{2,})\b"))
        {
            string token = idMatch.Groups[1].Value;
            if (!seenTypes.Add(token)) continue;

            if (typeToFileMap.TryGetValue(token, out var targetFiles))
            {
                string relKind = (token.StartsWith("I") && token.Length > 2 && char.IsUpper(token[1]))
                    ? "Interface"
                    : "Reference";

                foreach (var tf in targetFiles)
                {
                    recordEdge(srcRel, tf, relKind);
                }
            }
        }
    }

    private static void ComputePageRank(Dictionary<string, KnowledgeGraphNodeDto> nodes)
    {
        int n = nodes.Count;
        if (n == 0) return;

        const double damping = 0.85;
        const int iterations = 25;

        var ranks = nodes.Keys.ToDictionary(k => k, _ => 1.0 / n, StringComparer.OrdinalIgnoreCase);
        var nextRanks = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        for (int iter = 0; iter < iterations; iter++)
        {
            double danglingSum = 0;
            foreach (var node in nodes.Values)
            {
                if (node.OutDegree == 0)
                {
                    danglingSum += ranks[node.Id];
                }
            }

            foreach (var k in nodes.Keys)
            {
                nextRanks[k] = (1.0 - damping) / n + (damping * danglingSum / n);
            }

            foreach (var node in nodes.Values)
            {
                if (node.OutDegree == 0) continue;
                double share = damping * (ranks[node.Id] / node.OutDegree);
                foreach (var target in node.OutgoingTargets)
                {
                    if (nextRanks.ContainsKey(target))
                    {
                        nextRanks[target] += share;
                    }
                }
            }

            foreach (var kv in nextRanks)
            {
                ranks[kv.Key] = kv.Value;
            }
        }

        double maxRank = ranks.Values.DefaultIfEmpty(1.0).Max();
        if (maxRank <= 0) maxRank = 1.0;

        foreach (var node in nodes.Values)
        {
            // Normalize to a clean 0..100 score
            node.PageRank = Math.Round((ranks[node.Id] / maxRank) * 100.0, 1);
        }
    }

    private static List<List<string>> FindCircularDependencyCycles(Dictionary<string, KnowledgeGraphNodeDto> nodes)
    {
        int index = 0;
        var indices = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var lowlink = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var stack = new Stack<string>();
        var onStack = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var cycles = new List<List<string>>();

        void StrongConnect(string v)
        {
            indices[v] = index;
            lowlink[v] = index;
            index++;
            stack.Push(v);
            onStack.Add(v);

            if (nodes.TryGetValue(v, out var node))
            {
                foreach (var w in node.OutgoingTargets)
                {
                    if (!nodes.ContainsKey(w)) continue;
                    if (!indices.ContainsKey(w))
                    {
                        StrongConnect(w);
                        lowlink[v] = Math.Min(lowlink[v], lowlink[w]);
                    }
                    else if (onStack.Contains(w))
                    {
                        lowlink[v] = Math.Min(lowlink[v], indices[w]);
                    }
                }
            }

            if (lowlink[v] == indices[v])
            {
                var scc = new List<string>();
                string w;
                do
                {
                    w = stack.Pop();
                    onStack.Remove(w);
                    scc.Add(w);
                } while (!w.Equals(v, StringComparison.OrdinalIgnoreCase));

                if (scc.Count > 1)
                {
                    scc.Reverse();
                    scc.Add(scc[0]); // close the loop for clarity: A -> B -> A
                    cycles.Add(scc);
                }
            }
        }

        foreach (var k in nodes.Keys)
        {
            if (!indices.ContainsKey(k))
            {
                StrongConnect(k);
            }
        }

        return cycles;
    }

    private static string BuildMermaidArchitectureDiagram(
        List<KnowledgeGraphNodeDto> sortedNodes,
        List<KnowledgeGraphEdgeDto> allEdges)
    {
        // Pick up to 24 most connected/important nodes so the Mermaid chart stays clean and readable
        var topNodes = sortedNodes
            .Where(n => !n.IsOrphan)
            .Take(24)
            .ToList();

        if (topNodes.Count == 0)
        {
            topNodes = sortedNodes.Take(15).ToList();
        }

        var includedIds = new HashSet<string>(topNodes.Select(n => n.Id), StringComparer.OrdinalIgnoreCase);
        var nodeSafeId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < topNodes.Count; i++)
        {
            nodeSafeId[topNodes[i].Id] = $"N{i + 1}";
        }

        var sb = new StringBuilder();
        sb.AppendLine("flowchart LR");

        // Group nodes by cluster in subgraphs
        var byCluster = topNodes.GroupBy(n => n.Cluster, StringComparer.OrdinalIgnoreCase);
        int clusterIdx = 1;
        foreach (var grp in byCluster)
        {
            string sgId = $"C{clusterIdx++}";
            sb.AppendLine($"    subgraph {sgId}[\"{EscapeMermaidLabel(grp.Key)}\"]");
            foreach (var n in grp)
            {
                string nid = nodeSafeId[n.Id];
                string badge = n.IsGodNode ? " ★" : (n.IsModified ? " ●" : "");
                sb.AppendLine($"        {nid}[\"{EscapeMermaidLabel(n.FileName + badge)}\"]");
            }
            sb.AppendLine("    end");
        }

        int edgeCount = 0;
        foreach (var e in allEdges)
        {
            if (includedIds.Contains(e.Source) && includedIds.Contains(e.Target))
            {
                string sId = nodeSafeId[e.Source];
                string tId = nodeSafeId[e.Target];
                sb.AppendLine($"    {sId} --> {tId}");
                edgeCount++;
                if (edgeCount >= 45) break;
            }
        }

        return sb.ToString().TrimEnd();
    }

    private static string RenderKnowledgeGraphMarkdown(
        KnowledgeGraphResult res,
        string outputLanguage,
        bool includeSourceCode = false,
        int? maxTokens = null,
        string wsRoot = "")
    {
        string langCode = MarkdownGenerator.NormalizeLanguageCode(outputLanguage);
        string langDisplay = MarkdownGenerator.GetNaturalLanguageDisplayName(langCode);
        string langInstruction = MarkdownGenerator.GetLocalizedTaskInstruction(langCode);

        int graphTokensEst = Math.Max(100, res.TotalFiles * 35);
        double estSavings = res.RawCodebaseTokens > 0
            ? Math.Round(Math.Max(0.0, (1.0 - (graphTokensEst / (double)res.RawCodebaseTokens)) * 100.0), 1)
            : 0.0;

        var sb = new StringBuilder();
        sb.AppendLine(includeSourceCode
            ? "# Codebase Knowledge Graph & Source Code Context (Merged Architecture & Classes)"
            : "# Codebase Knowledge Graph (Graphify Architecture Map)");
        sb.AppendLine();
        sb.AppendLine("## Task");
        sb.AppendLine();
        sb.AppendLine(includeSourceCode
            ? "Use this deterministic AST & Dependency Knowledge Graph to understand the architecture, module boundaries, central hubs (\"God Nodes\"), and cross-file relationships of the codebase, with complete source code implementations included below."
            : "Use this deterministic AST & Dependency Knowledge Graph to understand the architecture, module boundaries, central hubs (\"God Nodes\"), and cross-file relationships of the codebase without needing to read every raw file.");
        if (langCode != "en")
        {
            sb.AppendLine();
            sb.AppendLine($"> **Response Language ({langDisplay})**: {langInstruction}");
        }
        sb.AppendLine();

        sb.AppendLine("## Executive Architecture Metrics");
        sb.AppendLine();
        sb.AppendLine($"- **Workspace**: `{res.WorkspaceName}`");
        sb.AppendLine($"- **Scope**: {res.ScopeDescription}");
        if (langCode != "en")
        {
            sb.AppendLine($"- **Response Language**: {langDisplay}");
        }
        sb.AppendLine($"- **Languages Detected**: {(res.Languages.Count > 0 ? string.Join(", ", res.Languages) : "Auto")}");
        sb.AppendLine($"- **Graph Scale**: **{res.TotalFiles}** source files | **{res.TotalEdges}** dependency edges | **{res.TotalSymbols}** exported symbols | **{res.TotalLines:N0}** lines of code");
        if (includeSourceCode)
        {
            sb.AppendLine($"- **Content Mode**: **Merged Knowledge Graph + Full Implementation Source Code**");
        }
        else
        {
            sb.AppendLine($"- **Token Efficiency**: Raw codebase is **~{res.RawCodebaseTokens:N0} tokens**; this Knowledge Graph compresses the structural map by **~{estSavings}%**");
        }
        sb.AppendLine();

        // God Nodes Table
        if (res.GodNodes.Count > 0)
        {
            sb.AppendLine("## High-Centrality Hubs (\"God Nodes\" & Core Bottlenecks)");
            sb.AppendLine();
            sb.AppendLine("These files have the highest PageRank centrality and incoming dependency fan-in. Changes to these files have the widest blast radius across the repository:");
            sb.AppendLine();
            sb.AppendLine("| Rank | File Path | Role | PageRank | Used By (In) | Depends On (Out) | Key Exported Symbols |");
            sb.AppendLine("| --- | --- | --- | --- | --- | --- | --- |");
            for (int i = 0; i < res.GodNodes.Count; i++)
            {
                var g = res.GodNodes[i];
                string syms = g.Symbols.Count > 0 ? string.Join(", ", g.Symbols.Take(4).Select(s => $"`{s}`")) : "-";
                sb.AppendLine($"| #{i + 1} | `{g.RelativePath}` | {g.Role} | **{g.PageRank:0.0}** | {g.InDegree} files | {g.OutDegree} files | {syms} |");
            }
            sb.AppendLine();
        }

        // Circular Dependencies (Tarjan SCC)
        sb.AppendLine("## Circular Dependency Analysis (Tarjan SCC)");
        sb.AppendLine();
        if (res.CircularCycles.Count == 0)
        {
            sb.AppendLine("- **Clean Architecture**: `0` circular file dependency cycles detected across the workspace.");
        }
        else
        {
            sb.AppendLine($"- **{res.CircularCycles.Count} Circular Dependency Cycle(s) Detected**:");
            foreach (var cycle in res.CircularCycles)
            {
                sb.AppendLine($"  - {string.Join(" -> ", cycle.Select(c => $"`{c}`"))}");
            }
        }
        sb.AppendLine();

        // Mermaid Diagram
        if (!string.IsNullOrWhiteSpace(res.MermaidDiagram))
        {
            sb.AppendLine("## Architecture Dependency Flowchart");
            sb.AppendLine();
            sb.AppendLine("```mermaid");
            sb.AppendLine(res.MermaidDiagram);
            sb.AppendLine("```");
            sb.AppendLine();
        }

        // Domain Clusters Overview
        if (res.Clusters.Count > 0)
        {
            sb.AppendLine("## Architectural Clusters & Modules");
            sb.AppendLine();
            sb.AppendLine("| Cluster / Module | Files | Lines of Code | Primary Files |");
            sb.AppendLine("| --- | --- | --- | --- |");
            foreach (var c in res.Clusters)
            {
                string sample = string.Join(", ", c.Files.Take(4).Select(f => $"`{Path.GetFileName(f)}`"));
                if (c.Files.Count > 4) sample += $", +{c.Files.Count - 4} more";
                sb.AppendLine($"| **{c.Name}** | {c.FileCount} | {c.TotalLines:N0} | {sample} |");
            }
            sb.AppendLine();
        }

        // Complete Queryable Adjacency & AST Signature Map
        sb.AppendLine("## Queryable Codebase Adjacency & AST Symbol Map");
        sb.AppendLine();
        foreach (var cluster in res.Clusters)
        {
            sb.AppendLine($"### Module: `{cluster.Name}` ({cluster.FileCount} files)");
            sb.AppendLine();
            var clusterNodes = res.Nodes
                .Where(n => n.Cluster.Equals(cluster.Name, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(n => n.PageRank)
                .ThenBy(n => n.RelativePath, StringComparer.OrdinalIgnoreCase);

            foreach (var n in clusterNodes)
            {
                string tags = "";
                if (n.IsGodNode) tags += " **[HUB / GOD NODE]**";
                if (n.IsModified) tags += " **[GIT MODIFIED]**";
                if (n.IsInCycle) tags += " **[IN CYCLE]**";

                sb.AppendLine($"- **`{n.RelativePath}`** ({n.Language}, {n.LineCount} lines, Role: *{n.Role}*, PageRank: {n.PageRank:0.0}){tags}");
                if (n.Symbols.Count > 0)
                {
                    sb.AppendLine($"  - **Symbols**: {string.Join(", ", n.Symbols.Select(s => $"`{s}`"))}");
                }
                if (n.OutgoingTargets.Count > 0)
                {
                    sb.AppendLine($"  - **Depends On ({n.OutDegree})**: {string.Join(", ", n.OutgoingTargets.Take(8).Select(t => $"`{t}`"))}{(n.OutDegree > 8 ? $" (+{n.OutDegree - 8} more)" : "")}");
                }
                if (n.IncomingSources.Count > 0)
                {
                    sb.AppendLine($"  - **Used By ({n.InDegree})**: {string.Join(", ", n.IncomingSources.Take(8).Select(s => $"`{s}`"))}{(n.InDegree > 8 ? $" (+{n.InDegree - 8} more)" : "")}");
                }
            }
            sb.AppendLine();
        }

        // Optional full source code implementation section
        if (includeSourceCode && !string.IsNullOrEmpty(wsRoot))
        {
            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine("## Full Source Code Implementation & Classes");
            sb.AppendLine();
            sb.AppendLine("Below is the full source code implementation of the classes and components within this architecture network:");
            sb.AppendLine();

            var estimator = new ApproximateTokenEstimator();
            int currentEstTokens = estimator.EstimateTokens(sb.ToString()).TokenCount;
            int budget = maxTokens ?? int.MaxValue;
            int codeFilesCount = 0;

            var codeNodes = res.Nodes
                .OrderByDescending(n => n.PageRank)
                .ThenBy(n => n.RelativePath, StringComparer.OrdinalIgnoreCase);

            foreach (var node in codeNodes)
            {
                string abs = Path.Combine(wsRoot, node.RelativePath);
                if (!File.Exists(abs)) continue;

                string raw;
                try { raw = File.ReadAllText(abs); }
                catch { continue; }

                string safeContent = SecurityScanner.RedactSecrets(raw);
                int fileTokens = estimator.EstimateTokens(safeContent).TokenCount;

                if (currentEstTokens + fileTokens > budget && codeFilesCount > 0)
                {
                    sb.AppendLine($"> ⚠️ *Token budget limit reached ({currentEstTokens:N0} tokens). Remaining {res.Nodes.Count - codeFilesCount} files omitted.*");
                    break;
                }

                string fenceLang = MapExtensionToFenceLang(Path.GetExtension(abs));
                string hubTag = node.IsGodNode ? " ★ [God Node]" : "";
                sb.AppendLine($"### File: `{node.RelativePath}` ({node.Language}, {node.LineCount} lines{hubTag})");
                sb.AppendLine();
                sb.AppendLine($"```{fenceLang}");
                sb.AppendLine(safeContent.TrimEnd());
                sb.AppendLine("```");
                sb.AppendLine();

                currentEstTokens += fileTokens;
                codeFilesCount++;
            }
        }

        return sb.ToString().TrimEnd() + Environment.NewLine;
    }

    private static string MapExtensionToFenceLang(string ext) => ext.ToLowerInvariant() switch
    {
        ".cs" => "csharp",
        ".ts" => "typescript",
        ".tsx" => "tsx",
        ".js" => "javascript",
        ".jsx" => "jsx",
        ".py" => "python",
        ".java" => "java",
        ".go" => "go",
        ".rs" => "rust",
        ".kt" or ".kts" => "kotlin",
        ".php" => "php",
        ".dart" => "dart",
        ".cpp" or ".cc" or ".cxx" or ".hpp" or ".h" or ".c" => "cpp",
        ".html" or ".cshtml" or ".razor" => "html",
        ".css" or ".scss" => "css",
        ".json" => "json",
        ".xml" => "xml",
        ".yaml" or ".yml" => "yaml",
        ".sh" => "bash",
        _ => ""
    };

    private static List<string> ExtractDeclaredTypes(string content)
    {
        var list = new List<string>();
        foreach (Match m in TypeDeclRegex.Matches(content))
        {
            string name = m.Groups[1].Value.Trim();
            if (!string.IsNullOrEmpty(name) && !IsIgnoredKeyword(name) && !list.Contains(name, StringComparer.Ordinal))
            {
                list.Add(name);
            }
        }
        return list;
    }

    private static List<string> ExtractDeclaredFunctions(string content, List<string> declaredTypes)
    {
        var list = new List<string>();
        foreach (Match m in FuncDeclRegex.Matches(content))
        {
            string name = m.Groups[1].Success ? m.Groups[1].Value.Trim() : m.Groups[2].Value.Trim();
            if (!string.IsNullOrEmpty(name) &&
                !IsIgnoredKeyword(name) &&
                !declaredTypes.Contains(name, StringComparer.Ordinal) &&
                !list.Contains(name, StringComparer.Ordinal))
            {
                list.Add(name);
            }
        }
        return list;
    }

    private static bool IsIgnoredKeyword(string word) =>
        word is "if" or "for" or "foreach" or "while" or "switch" or "catch" or "using" or "return" or "new" or "typeof" or "sizeof" or "nameof" or "await" or "lock" or "get" or "set" or "value";

    private static string DetermineClusterName(string relPath)
    {
        var parts = relPath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length <= 1) return "root";

        var dirParts = parts.Take(parts.Length - 1)
            .Where(p => !p.Equals("src", StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToList();

        return dirParts.Count > 0 ? string.Join("/", dirParts) : parts[0];
    }

    private static string ClassifyNodeRole(KnowledgeGraphNodeDto n)
    {
        string lowerName = n.FileName.ToLowerInvariant();
        string lowerPath = n.RelativePath.ToLowerInvariant();

        if (n.IsGodNode) return "God Node / Core Hub";
        if (lowerName.StartsWith("program.") || lowerName.StartsWith("main.") || lowerName.StartsWith("index.") || lowerName.StartsWith("extension."))
            return "Entrypoint";
        if (lowerName.StartsWith("i") && n.FileName.Length > 2 && char.IsUpper(n.FileName[1]) && n.Language == "CSharp")
            return "Interface / Contract";
        if (lowerPath.Contains("protocol") || lowerPath.Contains("model") || lowerName.Contains("types") || lowerName.Contains("dto"))
            return "Model / Protocol";
        if (lowerPath.Contains("service") || lowerPath.Contains("orchestrator") || lowerPath.Contains("manager"))
            return "Service / Orchestrator";
        if (lowerPath.Contains("worker") || lowerPath.Contains("analyzer") || lowerPath.Contains("adapter"))
            return "Analyzer / Worker";
        if (lowerPath.Contains("view") || lowerPath.Contains("component") || lowerPath.Contains("page") || lowerName.EndsWith(".tsx") || lowerName.EndsWith(".jsx") || lowerName.EndsWith(".vue") || lowerName.EndsWith(".razor"))
            return "UI / Component";
        if (n.IsOrphan) return "Standalone / Leaf";
        if (n.InDegree > 0 && n.OutDegree == 0) return "Shared Foundation";
        return "Module";
    }

    private static bool IsTestOrFixturePath(string relPath)
    {
        var parts = relPath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        foreach (var p in parts)
        {
            if (p.Equals("tests", StringComparison.OrdinalIgnoreCase) ||
                p.Equals("test", StringComparison.OrdinalIgnoreCase) ||
                p.Equals("__tests__", StringComparison.OrdinalIgnoreCase) ||
                p.Equals("fixtures", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return relPath.Contains(".test.", StringComparison.OrdinalIgnoreCase) ||
               relPath.Contains(".spec.", StringComparison.OrdinalIgnoreCase) ||
               relPath.EndsWith("-context.md", StringComparison.OrdinalIgnoreCase) ||
               relPath.EndsWith("-knowledge-graph.md", StringComparison.OrdinalIgnoreCase);
    }

    private static string StripFilePrefix(string nodeId) =>
        nodeId.StartsWith("file:", StringComparison.OrdinalIgnoreCase) ? nodeId.Substring(5) : nodeId;

    private static string EscapeMermaidLabel(string text) =>
        text.Replace("\"", "'").Replace("[", "(").Replace("]", ")");

    private static string MapExtensionToLanguage(string ext) =>
        ext switch
        {
            ".cs" => "CSharp",
            ".vb" => "VisualBasic",
            ".ts" or ".mts" or ".cts" => "TypeScript",
            ".tsx" => "TSX",
            ".js" or ".mjs" or ".cjs" => "JavaScript",
            ".jsx" => "JSX",
            ".vue" => "Vue",
            ".py" => "Python",
            ".java" => "Java",
            ".go" => "Go",
            ".rs" => "Rust",
            ".kt" or ".kts" => "Kotlin",
            ".php" => "Php",
            ".dart" => "Dart",
            ".cpp" or ".cc" or ".cxx" or ".hpp" or ".hxx" => "Cpp",
            ".c" or ".h" => "C",
            ".razor" or ".cshtml" => "Razor",
            ".html" or ".htm" => "HTML",
            ".css" => "CSS",
            ".scss" or ".sass" => "SCSS",
            _ => "Source"
        };
}
