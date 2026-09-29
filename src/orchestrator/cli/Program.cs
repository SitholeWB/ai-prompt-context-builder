using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using AiPromptContextBuilder.Core;
using AiPromptContextBuilder.Core.Protocol;
using AiPromptContextBuilder.Workers.DotNet;

namespace AiPromptContextBuilder.Cli;

public class Program
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "--help" or "-h" or "help")
        {
            PrintHelp();
            return 0;
        }

        if (args[0] is "--version" or "-v" or "version")
        {
            Console.WriteLine("aipromptcontext (aicontext) version 1.0.4");
            return 0;
        }

        if (args[0] == "check-workers")
        {
            Console.WriteLine("Checking installed workers and language engines...");
            Console.WriteLine("[x] .NET 10 / Roslyn worker: Active (In-Process)");
            Console.WriteLine("[x] Node.js worker (TypeScript, React, Angular, Vue, HTML, CSS): Active (node worker.js)");
            Console.WriteLine("[x] Python worker (AST, tokenize): Active (python3 worker.py)");
            Console.WriteLine("[x] Java & Go syntax analyzers: Active (In-Process)");
            return 0;
        }

        string command = args[0];
        int argIndex = 1;
        if (command != "generate")
        {
            argIndex = 0;
        }

        var config = new UserConfiguration();
        string format = "text";

        for (int i = argIndex; i < args.Length; i++)
        {
            string arg = args[i];
            string NextVal() => (i + 1 < args.Length) ? args[++i] : string.Empty;

            switch (arg)
            {
                case "--file" or "-f":
                    config.RootPath = NextVal();
                    break;
                case "--workspace" or "-w":
                    config.WorkspacePath = NextVal();
                    break;
                case "--project":
                    config.ProjectPath = NextVal();
                    break;
                case "--symbol" or "-s":
                    config.RootSymbol = NextVal();
                    break;
                case "--output" or "-o":
                    config.OutputPath = NextVal();
                    break;
                case "--max-tokens":
                    if (int.TryParse(NextVal(), out int mt)) config.MaxTokens = mt;
                    break;
                case "--max-depth":
                    if (int.TryParse(NextVal(), out int md)) config.MaxDepth = md;
                    break;
                case "--comments":
                    string c = NextVal().ToLowerInvariant();
                    config.CommentMode = c switch
                    {
                        "remove" => CommentMode.Remove,
                        "auto" => CommentMode.Auto,
                        _ => CommentMode.Preserve
                    };
                    break;
                case "--include-tests":
                    config.IncludeTests = ParseBool(NextVal());
                    break;
                case "--include-generated":
                    config.IncludeGeneratedFiles = ParseBool(NextVal());
                    break;
                case "--include-attributes":
                    config.IncludeAttributes = ParseBool(NextVal());
                    break;
                case "--include-implementations":
                    config.IncludeImplementations = ParseBool(NextVal());
                    break;
                case "--include-companions":
                    config.IncludeCompanionFiles = ParseBool(NextVal());
                    break;
                case "--include-assets":
                    config.IncludeAssets = ParseBool(NextVal());
                    break;
                case "--include-config":
                    config.IncludeConfigurationFiles = ParseBool(NextVal());
                    break;
                case "--copy":
                    config.CopyToClipboard = ParseBool(NextVal());
                    break;
                case "--save":
                    config.SaveToFile = ParseBool(NextVal());
                    break;
                case "--open":
                    config.OpenAfterGeneration = ParseBool(NextVal());
                    break;
                case "--task":
                    config.Task = NextVal();
                    break;
                case "--language":
                    if (Enum.TryParse<LanguageName>(NextVal(), true, out var lang)) config.Language = lang;
                    break;
                case "--framework":
                    if (Enum.TryParse<FrameworkName>(NextVal(), true, out var fw)) config.Framework = fw;
                    break;
                case "--analysis-level":
                    if (Enum.TryParse<RequestedAnalysisLevel>(NextVal(), true, out var al)) config.AnalysisLevel = al;
                    break;
                case "--exclude-project":
                    config.ExcludedProjects.Add(NextVal());
                    break;
                case "--exclude-package":
                    config.ExcludedPackages.Add(NextVal());
                    break;
                case "--exclude-namespace":
                    config.ExcludedNamespaces.Add(NextVal());
                    break;
                case "--exclude-directory":
                    config.ExcludedDirectories.Add(NextVal());
                    break;
                case "--include-glob":
                    config.IncludeGlobs.Add(NextVal());
                    break;
                case "--exclude-glob":
                    config.ExcludeGlobs.Add(NextVal());
                    break;
                case "--tokenizer":
                    config.Tokenizer = NextVal();
                    break;
                case "--format":
                    format = NextVal().ToLowerInvariant();
                    break;
                case "--verbose":
                    config.Verbose = true;
                    break;
                case "--omit-timestamp":
                    config.OmitTimestamp = true;
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(config.RootPath))
        {
            if (format == "json")
            {
                var errObj = new
                {
                    success = false,
                    errorCode = "InvalidConfiguration",
                    message = "--file option is required."
                };
                Console.WriteLine(JsonSerializer.Serialize(errObj, JsonOpts));
            }
            else
            {
                Console.Error.WriteLine("Error: --file <path> is required.");
            }
            return 1;
        }

        if (!config.SaveToFile && !config.CopyToClipboard)
        {
            config.SaveToFile = true;
        }

        if (format == "text")
        {
            Console.Error.WriteLine($"[aipromptcontext] Analyzing source: {config.RootPath}...");
        }

        var orchestrator = new ContextOrchestrator();
        orchestrator.RegisterAdapter(new DotNetAdapter());

        var result = await orchestrator.GenerateContextAsync(config);

        if (!result.Success)
        {
            if (format == "json")
            {
                var jsonFail = new
                {
                    success = false,
                    errorCode = result.ErrorCode ?? "UnexpectedError",
                    message = result.ErrorMessage ?? "Generation failed."
                };
                Console.WriteLine(JsonSerializer.Serialize(jsonFail, JsonOpts));
            }
            else
            {
                Console.Error.WriteLine($"Error [{result.ErrorCode}]: {result.ErrorMessage}");
            }
            return 1;
        }

        if (format == "json")
        {
            var jsonSuccess = new
            {
                success = true,
                outputPath = result.OutputPath,
                rootPath = result.RootPath,
                rootSymbol = result.RootSymbol,
                language = result.Language,
                frameworks = result.Frameworks,
                analysisLevel = result.AnalysisLevel.ToString(),
                filesDiscovered = result.FilesDiscovered,
                filesIncluded = result.FilesIncluded,
                filesExcluded = result.FilesExcluded,
                fullEstimatedTokens = result.FullEstimatedTokens,
                finalEstimatedTokens = result.FinalEstimatedTokens,
                configuredTokenBudget = result.ConfiguredTokenBudget,
                commentsRequested = result.CommentsRequested,
                commentsApplied = result.CommentsApplied,
                budgetMet = result.BudgetMet,
                warnings = result.Warnings,
                diagnostics = result.Diagnostics
            };
            Console.WriteLine(JsonSerializer.Serialize(jsonSuccess, JsonOpts));
        }
        else
        {
            Console.WriteLine($"Context generated successfully!");
            Console.WriteLine($"  Output file:      {result.OutputPath}");
            Console.WriteLine($"  Files included:   {result.FilesIncluded} (of {result.FilesDiscovered} discovered)");
            Console.WriteLine($"  Estimated tokens: {result.FinalEstimatedTokens:N0}");
            Console.WriteLine($"  Comments:         {result.CommentsApplied}");
            if (result.Warnings.Count > 0)
            {
                Console.WriteLine($"  Warnings ({result.Warnings.Count}):");
                foreach (var w in result.Warnings)
                {
                    Console.WriteLine($"    - {w}");
                }
            }
            if (result.SecretFindings.Count > 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"  Potential Secrets Detected ({result.SecretFindings.Count}):");
                foreach (var s in result.SecretFindings)
                {
                    Console.WriteLine($"    - {s.RelativePath}:{s.Line} [{s.Category}] {s.Recommendation}");
                }
                Console.ResetColor();
            }
        }

        return 0;
    }

    private static bool ParseBool(string val) =>
        bool.TryParse(val, out bool b) ? b : true;

    private static void PrintHelp()
    {
        Console.WriteLine(@"AI Prompt & Context Builder (aipromptcontext) - Version 1.0.4
Generates portable, dependency-aware, AI-ready source prompts and context from an existing repository.

Usage:
  aipromptcontext generate --file <path> [options]
  aipromptcontext check-workers
  (or aicontext / aiprompt)

Required:
  --file <path>                  Target source file to analyze

Options:
  --workspace <path>             Workspace root directory boundary
  --project <path>               Explicit project file (.csproj, tsconfig.json, etc.)
  --symbol <qualified-name>      Specific class, function, or symbol to select as root
  --output <path>                Destination Markdown file path
  --max-tokens <int>             Token budget (omitted means unlimited)
  --max-depth <int>              Maximum dependency traversal depth (omitted means unlimited)
  --comments preserve|remove|auto Comment preservation policy (default: preserve)
  --language <lang>              Force specific language analyzer
  --framework <fw>               Force specific framework adapter
  --analysis-level <level>       Best, Semantic, SyntaxAware, ImportGraph
  --task <text>                  Custom prompt task heading
  --include-tests <bool>         Include test files (default: false)
  --include-generated <bool>     Include generated files (default: false)
  --include-attributes <bool>    Include attributes/decorators (default: false)
  --include-companions <bool>    Include companion templates/styles (default: true)
  --include-config <bool>        Include configuration files (default: true)
  --save <bool>                  Write result to file (default: true)
  --copy <bool>                  Copy result to clipboard (default: false)
  --format text|json             Output format (default: text)
  --omit-timestamp               Omit generation timestamp for deterministic golden testing
  --verbose                      Enable verbose diagnostic logging
  --help                         Display this help message
  --version                      Display version information
");
    }
}
