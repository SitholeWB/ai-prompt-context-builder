using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AiPromptContextBuilder.Core.Budget;
using AiPromptContextBuilder.Core.Filters;
using AiPromptContextBuilder.Core.Graph;
using AiPromptContextBuilder.Core.Markdown;
using AiPromptContextBuilder.Core.Protocol;
using AiPromptContextBuilder.Core.Root;
using AiPromptContextBuilder.Core.Scoring;
using AiPromptContextBuilder.Core.Security;
using AiPromptContextBuilder.Core.Tokenizer;
using AiPromptContextBuilder.Core.Workers;
using AiPromptContextBuilder.Core.Workspace;

namespace AiPromptContextBuilder.Core;

public class OrchestrationResult
{
    public bool Success { get; set; }
    public string? OutputPath { get; set; }
    public string RootPath { get; set; } = string.Empty;
    public string? RootSymbol { get; set; }
    public string Language { get; set; } = string.Empty;
    public List<string> Frameworks { get; set; } = new();
    public CapabilityLevel AnalysisLevel { get; set; } = CapabilityLevel.Semantic;
    public int FilesDiscovered { get; set; }
    public int FilesIncluded { get; set; }
    public int FilesExcluded { get; set; }
    public int FullEstimatedTokens { get; set; }
    public int FinalEstimatedTokens { get; set; }
    public int? ConfiguredTokenBudget { get; set; }
    public string CommentsRequested { get; set; } = "Preserve";
    public string CommentsApplied { get; set; } = "Preserved";
    public bool BudgetMet { get; set; }
    public List<string> Warnings { get; set; } = new();
    public List<Diagnostic> Diagnostics { get; set; } = new();
    public List<SecretFinding> SecretFindings { get; set; } = new();
    public string? GeneratedMarkdown { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
}

public class ContextOrchestrator
{
    private readonly ITokenEstimator _tokenEstimator;
    private readonly List<ILanguageAdapter> _adapters = new();

    public ContextOrchestrator(ITokenEstimator? tokenEstimator = null)
    {
        _tokenEstimator = tokenEstimator ?? new ApproximateTokenEstimator();
        RegisterDefaultAdapters();
    }

    public void RegisterAdapter(ILanguageAdapter adapter)
    {
        _adapters.Insert(0, adapter); // Prepend so custom adapters have higher priority
    }

    private void RegisterDefaultAdapters()
    {
        _adapters.Add(new NodeWorkerAdapter());
        _adapters.Add(new PythonWorkerAdapter());
        _adapters.Add(new JavaAdapter());
        _adapters.Add(new GoAdapter());
        _adapters.Add(new RustAdapter());
        _adapters.Add(new KotlinAdapter());
        _adapters.Add(new PhpAdapter());
        _adapters.Add(new DartAdapter());
        _adapters.Add(new CppAdapter());
    }

    public async Task<OrchestrationResult> GenerateContextAsync(
        UserConfiguration config,
        CancellationToken cancellationToken = default)
    {
        string rootFullPath = Path.GetFullPath(config.RootPath);
        if (!File.Exists(rootFullPath))
        {
            return new OrchestrationResult
            {
                Success = false,
                ErrorCode = "FileNotFound",
                ErrorMessage = $"Target root file not found: {config.RootPath}"
            };
        }

        // 1. Workspace Discovery
        var discoveredWs = WorkspaceDiscovery.Discover(rootFullPath, config.WorkspacePath, config.ProjectPath);
        string wsRoot = discoveredWs.WorkspaceRoot;

        // 2. Validate Root Security (path traversal)
        try
        {
            SecurityScanner.ValidatePathInWorkspace(rootFullPath, wsRoot);
        }
        catch (Exception ex)
        {
            return new OrchestrationResult
            {
                Success = false,
                ErrorCode = "OutsideWorkspace",
                ErrorMessage = ex.Message
            };
        }

        string rootRelPath = Path.GetRelativePath(wsRoot, rootFullPath).Replace('\\', '/');

        // 3. Language & Framework Detection
        string ext = Path.GetExtension(rootFullPath).ToLowerInvariant();
        string detectedLang = DetectLanguage(ext, config.Language);
        var detectedFrameworks = new List<string>();

        var graph = new DependencyGraph();
        var allWarnings = new List<string>();
        var allDiagnostics = new List<Diagnostic>();

        // 4. Initial Worker Analysis
        var initialFragment = await AnalyzeWithAdapterAsync(rootFullPath, wsRoot, detectedLang, config, cancellationToken);
        graph.MergeFragment(initialFragment);
        allWarnings.AddRange(initialFragment.Warnings);
        allDiagnostics.AddRange(initialFragment.Diagnostics);

        // 5. Cross-Language Traversal
        var visitedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { rootFullPath };
        var filesToProcess = new Queue<string>(graph.GetAllFiles().Select(f => Path.Combine(wsRoot, f)));

        while (filesToProcess.Count > 0)
        {
            string currentFile = filesToProcess.Dequeue();
            if (!visitedFiles.Add(currentFile)) continue;
            if (!File.Exists(currentFile)) continue;

            string curExt = Path.GetExtension(currentFile).ToLowerInvariant();
            string curLang = DetectLanguage(curExt, LanguageName.Auto);

            if (!curLang.Equals(detectedLang, StringComparison.OrdinalIgnoreCase))
            {
                var secondaryFragment = await AnalyzeWithAdapterAsync(currentFile, wsRoot, curLang, config, cancellationToken);
                graph.MergeFragment(secondaryFragment);
                allWarnings.AddRange(secondaryFragment.Warnings);
                allDiagnostics.AddRange(secondaryFragment.Diagnostics);
            }
        }

        // 6. Root Target Resolution
        var rootFileNodes = graph.GetNodesByFile(rootRelPath);
        if (rootFileNodes.Count == 0)
        {
            var rfNode = new GraphNode
            {
                Id = $"file:{rootRelPath}",
                Kind = NodeKind.File,
                Language = detectedLang,
                AnalysisLevel = CapabilityLevel.Semantic,
                DisplayName = Path.GetFileName(rootFullPath),
                QualifiedName = rootRelPath,
                RelativePath = rootRelPath,
                SourceAvailable = true,
                Content = File.ReadAllText(rootFullPath)
            };
            graph.AddNode(rfNode);
            rootFileNodes = new List<GraphNode> { rfNode };
        }

        RootSelectionResult rootTarget;
        try
        {
            rootTarget = RootTargetResolver.Resolve(rootFileNodes.ToList(), rootRelPath, config.RootSymbol);
        }
        catch (Exception ex)
        {
            return new OrchestrationResult
            {
                Success = false,
                ErrorCode = "RootSymbolNotFound",
                ErrorMessage = ex.Message
            };
        }

        if (!rootTarget.IsSingleCandidate && string.IsNullOrEmpty(config.RootSymbol))
        {
            return new OrchestrationResult
            {
                Success = false,
                ErrorCode = "MultipleRootSymbols",
                ErrorMessage = $"Multiple eligible root symbols found in '{rootRelPath}'. Please specify one with --symbol: {string.Join(", ", rootTarget.Candidates)}"
            };
        }

        // 7. Importance Scoring
        ImportanceScorer.CalculateScores(graph, rootTarget.RootNodeId);

        // 8. Filters & Secret Scanning
        var candidateFiles = new List<EvaluatedFile>();
        var excludedFiles = new List<EvaluatedFile>();
        var allDiscoveredFiles = graph.GetAllFiles().ToList();
        var allSecretFindings = new List<SecretFinding>();

        foreach (var relPath in allDiscoveredFiles)
        {
            string absPath = Path.Combine(wsRoot, relPath);
            bool isRoot = relPath.Equals(rootRelPath, StringComparison.OrdinalIgnoreCase);
            var nodesInFile = graph.GetNodesByFile(relPath);
            int minDepth = nodesInFile.Count > 0 ? nodesInFile.Min(n => n.Depth) : 99;
            int maxImportance = nodesInFile.Count > 0 ? nodesInFile.Max(n => n.Importance) : 0;
            string originalContent = File.Exists(absPath) ? File.ReadAllText(absPath) : string.Empty;

            var filterEval = EcosystemFilters.EvaluatePrecedence(relPath, minDepth, isRoot, config, originalContent);

            var parentPaths = graph.GetInEdges($"file:{relPath}")
                .Select(e => graph.GetNode(e.SourceNodeId)?.RelativePath)
                .Where(p => !string.IsNullOrEmpty(p) && !p.Equals(relPath, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(p => p!)
                .ToList();

            bool isCompanion = nodesInFile.Any(n => n.AnalysisLevel == CapabilityLevel.CompanionFile) ||
                               relPath.EndsWith(".html") || relPath.EndsWith(".scss") || relPath.EndsWith(".css");

            var evaluatedFile = new EvaluatedFile
            {
                RelativePath = relPath,
                AbsolutePath = absPath,
                Nodes = nodesInFile.ToList(),
                IsRoot = isRoot,
                Depth = minDepth,
                Importance = maxImportance,
                OriginalContent = originalContent,
                ProcessedContent = originalContent,
                IsCompanion = isCompanion,
                ParentFilePaths = parentPaths
            };

            if (filterEval.Allowed)
            {
                var secrets = SecurityScanner.ScanForSecrets(relPath, originalContent);
                allSecretFindings.AddRange(secrets);
                candidateFiles.Add(evaluatedFile);
            }
            else
            {
                evaluatedFile.Included = false;
                evaluatedFile.ExclusionReason = filterEval.ExclusionReason;
                excludedFiles.Add(evaluatedFile);
            }
        }

        // 9. Token Budget Selection
        var budgetResult = TokenBudgetSelector.SelectFiles(candidateFiles, graph, config, _tokenEstimator);
        var totalExcluded = excludedFiles.Concat(budgetResult.ExcludedFiles).ToList();

        // 10. Generate Markdown
        var metadata = new MarkdownGenerationMetadata
        {
            RootPath = rootRelPath,
            RootSymbol = rootTarget.RootSymbol,
            WorkspaceName = Path.GetFileName(wsRoot) ?? "Workspace",
            Language = detectedLang,
            Framework = detectedFrameworks.Count > 0 ? string.Join(", ", detectedFrameworks) : (config.Framework != FrameworkName.Auto ? config.Framework.ToString() : "None"),
            AnalysisLevel = CapabilityLevel.Semantic,
            FilesDiscovered = allDiscoveredFiles.Count,
            Warnings = allWarnings,
            DependencyTreeText = graph.RenderDependencyTree(rootTarget.RootNodeId)
        };

        string markdown = MarkdownGenerator.Generate(metadata, budgetResult, totalExcluded, config);

        // 11. Save output file
        string? finalOutputPath = null;
        if (config.SaveToFile)
        {
            string outFilename = !string.IsNullOrEmpty(config.OutputPath)
                ? config.OutputPath
                : MarkdownGenerator.GetDefaultOutputFilename(rootRelPath);

            finalOutputPath = Path.IsPathRooted(outFilename) ? outFilename : Path.Combine(wsRoot, outFilename);
            string? outDir = Path.GetDirectoryName(finalOutputPath);
            if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
            {
                Directory.CreateDirectory(outDir);
            }
            File.WriteAllText(finalOutputPath, markdown);
        }

        return new OrchestrationResult
        {
            Success = true,
            OutputPath = finalOutputPath,
            RootPath = rootRelPath,
            RootSymbol = rootTarget.RootSymbol,
            Language = detectedLang,
            Frameworks = detectedFrameworks,
            AnalysisLevel = CapabilityLevel.Semantic,
            FilesDiscovered = allDiscoveredFiles.Count,
            FilesIncluded = budgetResult.IncludedFiles.Count,
            FilesExcluded = totalExcluded.Count,
            FullEstimatedTokens = budgetResult.FullEstimatedTokens,
            FinalEstimatedTokens = budgetResult.FinalEstimatedTokens,
            ConfiguredTokenBudget = config.MaxTokens,
            CommentsRequested = config.CommentMode.ToString(),
            CommentsApplied = budgetResult.CommentsApplied,
            BudgetMet = budgetResult.BudgetMet,
            Warnings = allWarnings,
            Diagnostics = allDiagnostics,
            SecretFindings = allSecretFindings,
            GeneratedMarkdown = markdown
        };
    }

    private async Task<DependencyGraphFragment> AnalyzeWithAdapterAsync(
        string filePath,
        string workspaceRoot,
        string language,
        UserConfiguration config,
        CancellationToken ct)
    {
        string ext = Path.GetExtension(filePath).ToLowerInvariant();
        var adapter = _adapters.FirstOrDefault(a => a.CanHandle(language, ext));
        if (adapter != null)
        {
            return await adapter.AnalyzeAsync(filePath, workspaceRoot, config, ct);
        }
        return new DependencyGraphFragment();
    }

    private string DetectLanguage(string ext, LanguageName explicitLang)
    {
        if (explicitLang != LanguageName.Auto) return explicitLang.ToString();

        return ext switch
        {
            ".cs" => "CSharp",
            ".vb" => "VisualBasic",
            ".ts" or ".mts" or ".cts" => "TypeScript",
            ".tsx" => "TSX",
            ".js" or ".mjs" or ".cjs" => "JavaScript",
            ".jsx" => "JSX",
            ".html" or ".htm" => "HTML",
            ".css" => "CSS",
            ".scss" or ".sass" => "SCSS",
            ".java" => "Java",
            ".py" => "Python",
            ".go" => "Go",
            ".razor" or ".cshtml" => "Razor",
            ".vue" => "Vue",
            ".rs" => "Rust",
            ".kt" or ".kts" => "Kotlin",
            ".php" => "Php",
            ".dart" => "Dart",
            ".cpp" or ".cc" or ".cxx" or ".hpp" or ".hxx" => "Cpp",
            ".c" or ".h" => "C",
            _ => "Unknown"
        };
    }
}

// Built-in Adapters in Core
public class NodeWorkerAdapter : ILanguageAdapter
{
    public string Id => "NodeWorker";
    public CapabilityLevel Capability => CapabilityLevel.Semantic;
    public IReadOnlyList<string> SupportedExtensions => new[]
    {
        ".ts", ".tsx", ".js", ".jsx", ".mjs", ".cjs", ".html", ".htm", ".css", ".scss", ".sass", ".vue"
    };

    public bool CanHandle(string language, string extension)
    {
        return SupportedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase) ||
               language is "TypeScript" or "JavaScript" or "JSX" or "TSX" or "HTML" or "CSS" or "SCSS" or "Sass" or "Vue";
    }

    public async Task<DependencyGraphFragment> AnalyzeAsync(string filePath, string workspaceRoot, UserConfiguration config, CancellationToken cancellationToken = default)
    {
        string workerJsPath = WorkerLocator.LocateWorkerScript("worker.js", "src/workers/node/host/worker.js");
        string? nodeExe = WorkerLocator.LocateNodeExecutable();

        if (string.IsNullOrEmpty(nodeExe) || !File.Exists(workerJsPath))
        {
            // Node.js is not installed or worker script is missing -> Fallback immediately to high-speed built-in syntax engine
            var fallbackFrag = BuiltInSyntaxAnalyzer.AnalyzeJsTs(filePath, workspaceRoot);
            fallbackFrag.Warnings.Add("Note: Node.js runtime was not detected. Built-in syntax engine was used to discover JSX/TypeScript/JavaScript dependencies. For deep semantic type resolution, you can install Node.js (https://nodejs.org).");
            return fallbackFrag;
        }

        try
        {
            var nodeReq = new WorkerProtocolRequest
            {
                Operation = "analyse",
                RootPath = filePath,
                WorkspacePath = workspaceRoot,
                Language = Path.GetExtension(filePath)
            };
            var nodeRes = await WorkerClient.ExecuteExternalWorkerAsync(nodeExe, $"\"{workerJsPath}\"", nodeReq, cancellationToken);

            if (nodeRes.Success && nodeRes.Graph != null && nodeRes.Graph.Nodes.Count > 0)
            {
                var builtInFrag = BuiltInSyntaxAnalyzer.AnalyzeJsTs(filePath, workspaceRoot);
                foreach (var node in builtInFrag.Nodes)
                {
                    if (!nodeRes.Graph.Nodes.Any(n => n.Id.Equals(node.Id, StringComparison.OrdinalIgnoreCase)))
                    {
                        nodeRes.Graph.Nodes.Add(node);
                    }
                }
                foreach (var edge in builtInFrag.Edges)
                {
                    if (!nodeRes.Graph.Edges.Any(e => e.SourceNodeId.Equals(edge.SourceNodeId, StringComparison.OrdinalIgnoreCase) &&
                                                      e.TargetNodeId.Equals(edge.TargetNodeId, StringComparison.OrdinalIgnoreCase)))
                    {
                        nodeRes.Graph.Edges.Add(edge);
                    }
                }
                return nodeRes.Graph;
            }

            // External worker ran but failed or returned empty response -> seamless fallback!
            var fallbackFrag = BuiltInSyntaxAnalyzer.AnalyzeJsTs(filePath, workspaceRoot);
            string detail = !string.IsNullOrWhiteSpace(nodeRes.ErrorMessage) ? $" ({nodeRes.ErrorMessage.Trim()})" : "";
            fallbackFrag.Warnings.Add($"Note: External Node.js worker encountered an issue{detail}. Built-in syntax engine was used to resolve dependencies.");
            return fallbackFrag;
        }
        catch (Exception ex)
        {
            var fallbackFrag = BuiltInSyntaxAnalyzer.AnalyzeJsTs(filePath, workspaceRoot);
            fallbackFrag.Warnings.Add($"Note: Node.js worker process failed ({ex.Message}). Built-in syntax engine was used to resolve dependencies.");
            return fallbackFrag;
        }
    }
}

public class PythonWorkerAdapter : ILanguageAdapter
{
    public string Id => "PythonWorker";
    public CapabilityLevel Capability => CapabilityLevel.Semantic;
    public IReadOnlyList<string> SupportedExtensions => new[] { ".py" };

    public bool CanHandle(string language, string extension)
    {
        return extension.Equals(".py", StringComparison.OrdinalIgnoreCase) ||
               language.Equals("Python", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<DependencyGraphFragment> AnalyzeAsync(string filePath, string workspaceRoot, UserConfiguration config, CancellationToken cancellationToken = default)
    {
        string workerPyPath = WorkerLocator.LocateWorkerScript("worker.py", "src/workers/python/aicontext/worker.py");
        string? pythonExe = WorkerLocator.LocatePythonExecutable();

        if (string.IsNullOrEmpty(pythonExe) || !File.Exists(workerPyPath))
        {
            // Python is not installed -> Fallback to built-in syntax engine
            var fallbackFrag = BuiltInSyntaxAnalyzer.AnalyzePython(filePath, workspaceRoot);
            fallbackFrag.Warnings.Add("Note: Python runtime was not detected. Built-in syntax engine was used to discover Python imports and dependencies. For deep AST resolution, you can install Python (https://python.org).");
            return fallbackFrag;
        }

        try
        {
            var pyReq = new WorkerProtocolRequest
            {
                Operation = "analyse",
                RootPath = filePath,
                WorkspacePath = workspaceRoot,
                Language = "Python"
            };
            var pyRes = await WorkerClient.ExecuteExternalWorkerAsync(pythonExe, $"\"{workerPyPath}\"", pyReq, cancellationToken);

            if (pyRes.Success && pyRes.Graph != null && pyRes.Graph.Nodes.Count > 0)
            {
                var builtInFrag = BuiltInSyntaxAnalyzer.AnalyzePython(filePath, workspaceRoot);
                foreach (var node in builtInFrag.Nodes)
                {
                    if (!pyRes.Graph.Nodes.Any(n => n.Id.Equals(node.Id, StringComparison.OrdinalIgnoreCase)))
                    {
                        pyRes.Graph.Nodes.Add(node);
                    }
                }
                foreach (var edge in builtInFrag.Edges)
                {
                    if (!pyRes.Graph.Edges.Any(e => e.SourceNodeId.Equals(edge.SourceNodeId, StringComparison.OrdinalIgnoreCase) &&
                                                    e.TargetNodeId.Equals(edge.TargetNodeId, StringComparison.OrdinalIgnoreCase)))
                    {
                        pyRes.Graph.Edges.Add(edge);
                    }
                }
                return pyRes.Graph;
            }

            var fallbackFrag = BuiltInSyntaxAnalyzer.AnalyzePython(filePath, workspaceRoot);
            string detail = !string.IsNullOrWhiteSpace(pyRes.ErrorMessage) ? $" ({pyRes.ErrorMessage.Trim()})" : "";
            fallbackFrag.Warnings.Add($"Note: External Python worker encountered an issue{detail}. Built-in syntax engine was used to resolve dependencies.");
            return fallbackFrag;
        }
        catch (Exception ex)
        {
            var fallbackFrag = BuiltInSyntaxAnalyzer.AnalyzePython(filePath, workspaceRoot);
            fallbackFrag.Warnings.Add($"Note: Python worker process failed ({ex.Message}). Built-in syntax engine was used to resolve dependencies.");
            return fallbackFrag;
        }
    }
}

public class JavaAdapter : ILanguageAdapter
{
    public string Id => "JavaAdapter";
    public CapabilityLevel Capability => CapabilityLevel.SyntaxAware;
    public IReadOnlyList<string> SupportedExtensions => new[] { ".java" };

    public bool CanHandle(string language, string extension) =>
        extension.Equals(".java", StringComparison.OrdinalIgnoreCase) || language.Equals("Java", StringComparison.OrdinalIgnoreCase);

    public Task<DependencyGraphFragment> AnalyzeAsync(string filePath, string workspaceRoot, UserConfiguration config, CancellationToken cancellationToken = default) =>
        Task.FromResult(JavaGoAnalyzer.AnalyzeJava(filePath, workspaceRoot));
}

public class GoAdapter : ILanguageAdapter
{
    public string Id => "GoAdapter";
    public CapabilityLevel Capability => CapabilityLevel.SyntaxAware;
    public IReadOnlyList<string> SupportedExtensions => new[] { ".go" };

    public bool CanHandle(string language, string extension) =>
        extension.Equals(".go", StringComparison.OrdinalIgnoreCase) || language.Equals("Go", StringComparison.OrdinalIgnoreCase);

    public Task<DependencyGraphFragment> AnalyzeAsync(string filePath, string workspaceRoot, UserConfiguration config, CancellationToken cancellationToken = default) =>
        Task.FromResult(JavaGoAnalyzer.AnalyzeGo(filePath, workspaceRoot));
}
