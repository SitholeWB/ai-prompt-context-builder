using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AiPromptContextBuilder.Core;
using AiPromptContextBuilder.Core.Budget;
using AiPromptContextBuilder.Core.Comments;
using AiPromptContextBuilder.Core.Graph;
using AiPromptContextBuilder.Core.Markdown;
using AiPromptContextBuilder.Core.Protocol;
using AiPromptContextBuilder.Core.Scoring;
using AiPromptContextBuilder.Core.Security;
using AiPromptContextBuilder.Core.Tokenizer;
using AiPromptContextBuilder.Workers.DotNet;

namespace AiPromptContextBuilder.Tests;

public class Program
{
    private static int _passed = 0;
    private static int _failed = 0;

    public static async Task<int> Main()
    {
        Console.WriteLine("=================================================");
        Console.WriteLine(" AI Prompt & Context Builder - Automated Tests    ");
        Console.WriteLine("=================================================");

        RunTest("1. Neutral Graph: Multi-Edges, Cycles, Tree Rendering", TestNeutralGraph);
        RunTest("2. Scoring: Importance Weights & Depth Penalties", TestImportanceScoring);
        RunTest("3. Token Estimation: Approximate Heuristic", TestTokenEstimator);
        RunTest("4. Comment Removal: Docstring & String Preservation", TestCommentRemoval);
        RunTest("5. Security: Secret Scanner & Traversal Prevention", TestSecurityScanner);
        RunTest("6. Markdown: Dynamic Fence Safety & Section Output", TestMarkdownFenceSafety);
        RunTest("7. .NET / Roslyn Analyzer: C#, Interfaces, Constructors", TestDotNetAnalyzer);
        RunTest("8. Blazor / Razor Analyzer: Companions & Injections", TestBlazorAnalyzer);
        await RunTestAsync("9. Node Worker: Angular Template & SCSS Traversal", TestNodeWorkerAngular);
        await RunTestAsync("10. Node Worker: React JSX Component Dependencies", TestNodeWorkerReact);
        await RunTestAsync("11. Python Worker: AST Imports & Docstrings", TestPythonWorker);
        RunTest("12. Budget Selection: Coherent Chains & Auto Comment Mode", TestBudgetSelection);
        await RunTestAsync("13. End-to-End CLI: Full Context Generation", TestCliEndToEnd);
        await RunTestAsync("14. C# Member Access: Constant Interface Reference Traversal", TestConstantInterfaceMemberAccess);

        Console.WriteLine("=================================================");
        Console.WriteLine($"Tests Completed: {_passed} Passed, {_failed} Failed");
        Console.WriteLine("=================================================");

        return _failed == 0 ? 0 : 1;
    }

    private static void RunTest(string name, Action test)
    {
        try
        {
            test();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[PASS] {name}");
            Console.ResetColor();
            _passed++;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[FAIL] {name}: {ex.Message}");
            Console.ResetColor();
            _failed++;
        }
    }

    private static async Task RunTestAsync(string name, Func<Task> test)
    {
        try
        {
            await test();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[PASS] {name}");
            Console.ResetColor();
            _passed++;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[FAIL] {name}: {ex.Message}");
            Console.ResetColor();
            _failed++;
        }
    }

    private static void TestNeutralGraph()
    {
        var graph = new DependencyGraph();
        graph.AddNode(new GraphNode { Id = "A", DisplayName = "Root.cs", RelativePath = "src/Root.cs" });
        graph.AddNode(new GraphNode { Id = "B", DisplayName = "DepB.cs", RelativePath = "src/DepB.cs" });
        graph.AddNode(new GraphNode { Id = "C", DisplayName = "DepC.cs", RelativePath = "src/DepC.cs" });

        // Multi-edges between same source and target with distinct relationships
        graph.AddEdge(new GraphEdge { SourceNodeId = "A", TargetNodeId = "B", Relationship = RelationshipType.ConstructorDependency, Confidence = Confidence.Verified });
        graph.AddEdge(new GraphEdge { SourceNodeId = "A", TargetNodeId = "B", Relationship = RelationshipType.Interface, Confidence = Confidence.Verified });
        graph.AddEdge(new GraphEdge { SourceNodeId = "B", TargetNodeId = "C", Relationship = RelationshipType.Import, Confidence = Confidence.High });
        graph.AddEdge(new GraphEdge { SourceNodeId = "C", TargetNodeId = "A", Relationship = RelationshipType.Import, Confidence = Confidence.Medium }); // Cycle

        var outEdges = graph.GetOutEdges("A");
        Assert(outEdges.Count == 2, $"Expected 2 distinct edges from A to B, got {outEdges.Count}");

        var bfs = graph.TraverseBfs("A");
        Assert(bfs.Count == 3, $"Expected 3 nodes in BFS traversal, got {bfs.Count}");
        Assert(bfs[0].Node.Id == "A" && bfs[0].Depth == 0, "Root should be at depth 0");
        Assert(bfs[1].Node.Id == "B" && bfs[1].Depth == 1, "DepB should be at depth 1");
        Assert(bfs[2].Node.Id == "C" && bfs[2].Depth == 2, "DepC should be at depth 2");

        string tree = graph.RenderDependencyTree("A");
        Assert(tree.Contains("src/DepB.cs [constructordependency, verified]"), "Tree should format B relationship");
        Assert(tree.Contains("(circular)"), "Tree should annotate circular reference");
    }

    private static void TestImportanceScoring()
    {
        var graph = new DependencyGraph();
        graph.AddNode(new GraphNode { Id = "Root", RelativePath = "Root.cs" });
        graph.AddNode(new GraphNode { Id = "Service", RelativePath = "Service.cs" });
        graph.AddNode(new GraphNode { Id = "Helper", RelativePath = "Helper.cs" });

        graph.AddEdge(new GraphEdge { SourceNodeId = "Root", TargetNodeId = "Service", Relationship = RelationshipType.ConstructorDependency, Confidence = Confidence.Verified });
        graph.AddEdge(new GraphEdge { SourceNodeId = "Service", TargetNodeId = "Helper", Relationship = RelationshipType.MethodCall, Confidence = Confidence.High });

        var scores = ImportanceScorer.CalculateScores(graph, "Root");
        Assert(scores["Root"] == 1000, "Root score should be 1000");
        Assert(scores["Service"] > scores["Helper"], "Direct constructor dependency should outscore deeper helper");
    }

    private static void TestTokenEstimator()
    {
        var estimator = new ApproximateTokenEstimator();
        string sample = "public class CustomerService : ICustomerService { public void Process() { } }";
        var res = estimator.EstimateTokens(sample);
        Assert(res.TokenCount > 0, "Token count should be positive");
        Assert(res.IsEstimated, "IsEstimated should be true");
    }

    private static void TestCommentRemoval()
    {
        // C#
        string csCode = "/* header */ public class Foo { // line comment\n public string S = \"// not a comment\"; }";
        string csClean = CommentHandler.RemoveCSharpComments(csCode);
        Assert(!csClean.Contains("/* header */"), "Block comment removed");
        Assert(!csClean.Contains("// line comment"), "Line comment removed");
        Assert(csClean.Contains("\"// not a comment\""), "String literal preserved");

        // Python docstring preservation
        string pyCode = "# Shebang or comment\n\"\"\"Module docstring.\"\"\"\ndef foo():\n    # Inner comment\n    return 42";
        string pyClean = CommentHandler.RemovePythonComments(pyCode);
        Assert(pyClean.Contains("\"\"\"Module docstring.\"\"\""), "Python docstring must be preserved");
        Assert(!pyClean.Contains("# Inner comment"), "Python line comment must be removed");
    }

    private static void TestSecurityScanner()
    {
        string content = "var key = \"api_key = 'abcdef1234567890abcdef1234567890'\";\nvar pwd = \"Password=SecretPass123;Server=db;\";";
        var findings = SecurityScanner.ScanForSecrets("src/Secret.cs", content);
        Assert(findings.Count >= 2, $"Expected at least 2 findings, got {findings.Count}");

        // Path Traversal check
        try
        {
            SecurityScanner.ValidatePathInWorkspace("/etc/passwd", "/home/workspace");
            Assert(false, "Should have thrown path outside workspace error");
        }
        catch (InvalidOperationException)
        {
            // Expected
        }
    }

    private static void TestMarkdownFenceSafety()
    {
        string codeWithBackticks = "markdown example:\n```json\n{\"id\": 1}\n```";
        var (openFence, closeFence) = MarkdownGenerator.CreateSafeFence(codeWithBackticks, "markdown");
        Assert(openFence.StartsWith("````markdown"), "Fence should have 4 backticks to safely wrap 3 internal backticks");
        Assert(closeFence == "````", "Close fence should match length");
    }

    private static string ResolveFixture(string relativePath)
    {
        var candidates = new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory };
        foreach (var startDir in candidates)
        {
            var dir = new DirectoryInfo(startDir);
            while (dir != null)
            {
                string target = Path.Combine(dir.FullName, relativePath);
                if (File.Exists(target) || Directory.Exists(target))
                    return Path.GetFullPath(target);
                dir = dir.Parent;
            }
        }
        return Path.GetFullPath(relativePath);
    }

    private static void TestDotNetAnalyzer()
    {
        string root = ResolveFixture("tests/fixtures/dotnet/CustomerService.cs");
        string ws = ResolveFixture("tests/fixtures/dotnet");
        var analyzer = new DotNetAnalyzer();
        var fragment = analyzer.Analyze(root, ws, new UserConfiguration());

        Assert(fragment.Nodes.Any(n => n.DisplayName == "CustomerService"), "CustomerService class discovered");
        Assert(fragment.Nodes.Any(n => n.DisplayName == "ICustomerService"), "ICustomerService interface discovered");
        Assert(fragment.Nodes.Any(n => n.DisplayName == "CustomerRepository"), "CustomerRepository constructor dependency discovered");
    }

    private static async Task TestConstantInterfaceMemberAccess()
    {
        string root = ResolveFixture("tests/fixtures/dotnet/FanOptionHandler.cs");
        string ws = ResolveFixture("tests/fixtures/dotnet");
        var analyzer = new DotNetAnalyzer();
        var fragment = analyzer.Analyze(root, ws, new UserConfiguration());

        Assert(fragment.Nodes.Any(n => n.DisplayName == "FanOptionHandler"), "FanOptionHandler class discovered");
        Assert(fragment.Nodes.Any(n => n.DisplayName == "CommonValues"), "CommonValues interface discovered via member access");
        Assert(fragment.Edges.Any(e => e.TargetNodeId.Contains("CommonValues.cs")), "Edge to CommonValues.cs discovered");

        var orchestrator = new ContextOrchestrator();
        orchestrator.RegisterAdapter(new DotNetAdapter());
        var result = await orchestrator.GenerateContextAsync(new UserConfiguration
        {
            RootPath = root,
            WorkspacePath = ws,
            Language = LanguageName.CSharp
        });

        Assert(result.Success, $"Orchestration succeeded: [{result.ErrorCode}] {result.ErrorMessage}");
        Assert(result.GeneratedMarkdown!.Contains("CommonValues.cs"), "CommonValues.cs included in generated Markdown context");
        Assert(result.GeneratedMarkdown.Contains("Fan_Name"), "Fan_Name constant included in generated Markdown context");
    }

    private static void TestBlazorAnalyzer()
    {
        string root = ResolveFixture("tests/fixtures/dotnet/CustomerCard.razor");
        string ws = ResolveFixture("tests/fixtures/dotnet");
        var analyzer = new DotNetAnalyzer();
        var fragment = analyzer.Analyze(root, ws, new UserConfiguration());

        Assert(fragment.Nodes.Any(n => n.RelativePath.EndsWith("CustomerCard.razor")), "Razor component discovered");
        Assert(fragment.Edges.Any(e => e.Relationship == RelationshipType.CodeBehind), "Companion code-behind discovered");
        Assert(fragment.Edges.Any(e => e.Relationship == RelationshipType.Style), "Scoped CSS companion discovered");
        Assert(fragment.Edges.Any(e => e.Relationship == RelationshipType.Service), "Injected service discovered");
    }

    private static async Task TestNodeWorkerAngular()
    {
        string root = ResolveFixture("tests/fixtures/node/customer.component.ts");
        string ws = ResolveFixture("tests/fixtures/node");
        var adapter = new NodeWorkerAdapter();
        var fragment = await adapter.AnalyzeAsync(root, ws, new UserConfiguration());

        Assert(fragment.Nodes.Any(n => n.RelativePath.EndsWith("customer.component.ts")), "Component TS discovered");
        Assert(fragment.Edges.Any(e => e.Relationship == RelationshipType.Template), "templateUrl HTML companion discovered");
        Assert(fragment.Edges.Any(e => e.Relationship == RelationshipType.Style), "styleUrls SCSS companion discovered");
        Assert(fragment.Nodes.Any(n => n.RelativePath.EndsWith("_variables.scss")), "Transitive SCSS partial discovered");
    }

    private static async Task TestNodeWorkerReact()
    {
        string root = ResolveFixture("tests/fixtures/node/CustomerPage.tsx");
        string ws = ResolveFixture("tests/fixtures/node");
        var adapter = new NodeWorkerAdapter();
        var fragment = await adapter.AnalyzeAsync(root, ws, new UserConfiguration());

        Assert(fragment.Nodes.Any(n => n.RelativePath.EndsWith("CustomerPage.tsx")), "React Page discovered");
        Assert(fragment.Nodes.Any(n => n.RelativePath.EndsWith("CustomerWidget.tsx")), "Referenced JSX widget discovered");
    }

    private static async Task TestPythonWorker()
    {
        string root = ResolveFixture("tests/fixtures/python/service.py");
        string ws = ResolveFixture("tests/fixtures/python");
        var adapter = new PythonWorkerAdapter();
        var fragment = await adapter.AnalyzeAsync(root, ws, new UserConfiguration());

        Assert(fragment.Nodes.Any(n => n.RelativePath.EndsWith("service.py")), "Python service discovered");
        Assert(fragment.Nodes.Any(n => n.RelativePath.EndsWith("models.py")), "Imported models.py discovered");
    }

    private static void TestBudgetSelection()
    {
        var estimator = new ApproximateTokenEstimator();
        var candidates = new List<EvaluatedFile>
        {
            new EvaluatedFile { RelativePath = "Root.cs", IsRoot = true, Depth = 0, Importance = 1000, OriginalContent = "class Root {}", ParentFilePaths = new() },
            new EvaluatedFile { RelativePath = "Dep1.cs", IsRoot = false, Depth = 1, Importance = 100, OriginalContent = "class Dep1 {}", ParentFilePaths = new() { "Root.cs" } },
            new EvaluatedFile { RelativePath = "Dep2.cs", IsRoot = false, Depth = 2, Importance = 50, OriginalContent = "class Dep2 {}", ParentFilePaths = new() { "Dep1.cs" } }
        };

        var config = new UserConfiguration { MaxTokens = 50000 };
        var result = TokenBudgetSelector.SelectFiles(candidates, new DependencyGraph(), config, estimator);

        Assert(result.IncludedFiles.Count == 3, "All files should fit comfortably in 50k budget");
        Assert(result.BudgetMet, "Budget met should be true");
    }

    private static async Task TestCliEndToEnd()
    {
        string root = Path.GetFullPath("tests/fixtures/dotnet/CustomerService.cs");
        string outPath = Path.GetFullPath("customer-service-context.md");
        if (File.Exists(outPath)) File.Delete(outPath);

        var config = new UserConfiguration
        {
            RootPath = root,
            OutputPath = outPath,
            SaveToFile = true,
            OmitTimestamp = true
        };

        var orchestrator = new ContextOrchestrator();
        orchestrator.RegisterAdapter(new DotNetAdapter());
        var res = await orchestrator.GenerateContextAsync(config);

        Assert(res.Success, $"Generation should succeed, got error: {res.ErrorMessage}");
        Assert(File.Exists(outPath), "Output markdown file should exist");

        string content = File.ReadAllText(outPath);
        Assert(content.Contains("# AI Code Context"), "Header present");
        Assert(content.Contains("## Task"), "Task section present");
        Assert(content.Contains("## Context Rules"), "Rules present");
        Assert(content.Contains("## Dependency Tree"), "Dependency tree present");
        Assert(content.Contains("## Included Files"), "Included files list present");
        Assert(content.Contains($"### File: `{res.RootPath}`"), "Complete source file included");
        Assert(content.Contains("CustomerRepository"), "Complete repository dependency included");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
