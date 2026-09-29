using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AiPromptContextBuilder.Core.Protocol;

namespace AiPromptContextBuilder.Core.Workers;

public static class ExtendedLanguageAnalyzers
{
    // ==========================================
    // 1. RUST ANALYZER (.rs)
    // ==========================================
    public static DependencyGraphFragment AnalyzeRust(string rootPath, string workspaceRoot)
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
            string curDir = Path.GetDirectoryName(curFile) ?? workspaceRoot;

            fragment.Nodes.Add(new GraphNode
            {
                Id = fileNodeId,
                Kind = NodeKind.File,
                Language = "Rust",
                AnalysisLevel = CapabilityLevel.SyntaxAware,
                DisplayName = Path.GetFileName(curFile),
                QualifiedName = rel,
                RelativePath = rel,
                SourceAvailable = true,
                Content = content
            });

            // 1.1 Modules: `mod foo;` or `pub mod foo;`
            var modMatches = Regex.Matches(content, @"^\s*(?:pub\s+)?mod\s+([A-Za-z0-9_]+)\s*;", RegexOptions.Multiline);
            foreach (Match m in modMatches)
            {
                string modName = m.Groups[1].Value;
                string[] candidates = new[]
                {
                    Path.Combine(curDir, $"{modName}.rs"),
                    Path.Combine(curDir, modName, "mod.rs")
                };

                foreach (var cand in candidates)
                {
                    if (File.Exists(cand))
                    {
                        AddEdgeAndQueue(fragment, fileNodeId, cand, RelationshipType.Import, workspaceRoot, visited, queue);
                        break;
                    }
                }
            }

            // 1.2 Uses: `use crate::foo::bar;`, `use super::bar;`, `use foo::Bar;`
            var useMatches = Regex.Matches(content, @"^\s*(?:pub\s+)?use\s+([A-Za-z0-9_:]+)(?:::\{([^}]+)\})?\s*;", RegexOptions.Multiline);
            foreach (Match m in useMatches)
            {
                string usePath = m.Groups[1].Value;
                var segments = usePath.Split(new[] { "::" }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var seg in segments)
                {
                    if (seg is "crate" or "super" or "self") continue;

                    var localFiles = Directory.GetFiles(workspaceRoot, $"{seg}.rs", SearchOption.AllDirectories);
                    if (localFiles.Length > 0)
                    {
                        foreach (var lf in localFiles)
                        {
                            AddEdgeAndQueue(fragment, fileNodeId, lf, RelationshipType.Import, workspaceRoot, visited, queue);
                        }
                        break;
                    }
                }
            }

            // 1.3 Trait Implementations: `impl Trait for Struct`
            var implMatches = Regex.Matches(content, @"impl(?:<[^>]+>)?\s+([A-Za-z0-9_]+)\s+for\s+([A-Za-z0-9_]+)");
            foreach (Match m in implMatches)
            {
                string traitName = m.Groups[1].Value;
                string structName = m.Groups[2].Value;

                foreach (var name in new[] { traitName, structName })
                {
                    var matching = Directory.GetFiles(workspaceRoot, $"{name.ToLowerInvariant()}.rs", SearchOption.AllDirectories);
                    if (matching.Length == 0)
                    {
                        matching = Directory.GetFiles(workspaceRoot, $"{name}.rs", SearchOption.AllDirectories);
                    }
                    foreach (var mf in matching)
                    {
                        AddEdgeAndQueue(fragment, fileNodeId, mf, RelationshipType.Implementation, workspaceRoot, visited, queue);
                    }
                }
            }

            // 1.4 Macros & Derives: `#[derive(..., MyMacro, ...)]`
            var deriveMatches = Regex.Matches(content, @"#\[derive\(([^)]+)\)\]");
            foreach (Match dm in deriveMatches)
            {
                var traits = dm.Groups[1].Value.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var t in traits)
                {
                    string cleanTrait = t.Trim();
                    if (cleanTrait is "Debug" or "Clone" or "Copy" or "Default" or "PartialEq" or "Eq" or "PartialOrd" or "Ord" or "Hash" or "Serialize" or "Deserialize")
                        continue;

                    var matching = Directory.GetFiles(workspaceRoot, $"{cleanTrait.ToLowerInvariant()}.rs", SearchOption.AllDirectories);
                    foreach (var mf in matching)
                    {
                        AddEdgeAndQueue(fragment, fileNodeId, mf, RelationshipType.Attribute, workspaceRoot, visited, queue);
                    }
                }
            }
        }

        return fragment;
    }

    // ==========================================
    // 2. KOTLIN ANALYZER (.kt, .kts)
    // ==========================================
    public static DependencyGraphFragment AnalyzeKotlin(string rootPath, string workspaceRoot)
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
                Language = "Kotlin",
                AnalysisLevel = CapabilityLevel.SyntaxAware,
                DisplayName = Path.GetFileName(curFile),
                QualifiedName = rel,
                RelativePath = rel,
                SourceAvailable = true,
                Content = content
            });

            // 2.1 Imports: `import com.example.repository.OrderRepository`
            var importMatches = Regex.Matches(content, @"^\s*import\s+([A-Za-z0-9_.]+)", RegexOptions.Multiline);
            foreach (Match m in importMatches)
            {
                string imported = m.Groups[1].Value;
                var parts = imported.Split('.');
                for (int i = parts.Length - 1; i >= 0; i--)
                {
                    string candidate = parts[i];
                    if (string.IsNullOrEmpty(candidate) || candidate == "*") continue;

                    var localFiles = Directory.GetFiles(workspaceRoot, $"{candidate}.kt", SearchOption.AllDirectories);
                    if (localFiles.Length > 0)
                    {
                        foreach (var lf in localFiles)
                        {
                            AddEdgeAndQueue(fragment, fileNodeId, lf, RelationshipType.Import, workspaceRoot, visited, queue);
                        }
                        break;
                    }
                }
            }

            // 2.2 Extension Functions: `fun Order.calculateTax()` or invocations `.toSlug()`
            var extDefMatches = Regex.Matches(content, @"fun\s+(?:<[^>]+>\s+)?([A-Za-z0-9_]+)\.([A-Za-z0-9_]+)\s*\(");
            foreach (Match ed in extDefMatches)
            {
                string targetType = ed.Groups[1].Value;
                var matching = Directory.GetFiles(workspaceRoot, $"{targetType}.kt", SearchOption.AllDirectories);
                foreach (var mf in matching)
                {
                    AddEdgeAndQueue(fragment, fileNodeId, mf, RelationshipType.ExtensionMethod, workspaceRoot, visited, queue);
                }
            }

            // 2.3 Inheritance & Interfaces: `class OrderService : IOrderService, BaseService()`
            var classInheritance = Regex.Matches(content, @"class\s+([A-Za-z0-9_]+)(?:<[^>]+>)?\s*:\s*([^{]+)\{");
            foreach (Match ci in classInheritance)
            {
                string bases = ci.Groups[2].Value;
                var baseParts = Regex.Matches(bases, @"([A-Za-z0-9_]+)");
                foreach (Match bp in baseParts)
                {
                    string baseName = bp.Groups[1].Value;
                    var matching = Directory.GetFiles(workspaceRoot, $"{baseName}.kt", SearchOption.AllDirectories);
                    foreach (var mf in matching)
                    {
                        AddEdgeAndQueue(fragment, fileNodeId, mf, RelationshipType.Implementation, workspaceRoot, visited, queue);
                    }
                }
            }
        }

        return fragment;
    }

    // ==========================================
    // 3. PHP ANALYZER (.php)
    // ==========================================
    public static DependencyGraphFragment AnalyzePhp(string rootPath, string workspaceRoot)
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
                Language = "Php",
                AnalysisLevel = CapabilityLevel.SyntaxAware,
                DisplayName = Path.GetFileName(curFile),
                QualifiedName = rel,
                RelativePath = rel,
                SourceAvailable = true,
                Content = content
            });

            // 3.1 `use App\Repositories\OrderRepository;` or `use App\Traits\SoftDeletes;`
            var useMatches = Regex.Matches(content, @"^\s*use\s+([A-Za-z0-9_\\]+)(?:\s+as\s+[A-Za-z0-9_]+)?\s*;", RegexOptions.Multiline);
            foreach (Match m in useMatches)
            {
                string fullNamespace = m.Groups[1].Value;
                string className = fullNamespace.Split('\\').Last();
                if (string.IsNullOrEmpty(className)) continue;

                var localFiles = Directory.GetFiles(workspaceRoot, $"{className}.php", SearchOption.AllDirectories);
                foreach (var lf in localFiles)
                {
                    AddEdgeAndQueue(fragment, fileNodeId, lf, RelationshipType.Import, workspaceRoot, visited, queue);
                }
            }

            // 3.2 Inheritance & Implementations: `class OrderService extends BaseService implements IOrderService`
            var classMatches = Regex.Matches(content, @"class\s+([A-Za-z0-9_]+)(?:\s+extends\s+([A-Za-z0-9_]+))?(?:\s+implements\s+([A-Za-z0-9_,\s]+))?");
            foreach (Match cm in classMatches)
            {
                if (cm.Groups[2].Success)
                {
                    string baseClass = cm.Groups[2].Value;
                    var localFiles = Directory.GetFiles(workspaceRoot, $"{baseClass}.php", SearchOption.AllDirectories);
                    foreach (var lf in localFiles)
                    {
                        AddEdgeAndQueue(fragment, fileNodeId, lf, RelationshipType.BaseType, workspaceRoot, visited, queue);
                    }
                }
                if (cm.Groups[3].Success)
                {
                    var interfaces = cm.Groups[3].Value.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var iface in interfaces)
                    {
                        var localFiles = Directory.GetFiles(workspaceRoot, $"{iface}.php", SearchOption.AllDirectories);
                        foreach (var lf in localFiles)
                        {
                            AddEdgeAndQueue(fragment, fileNodeId, lf, RelationshipType.Implementation, workspaceRoot, visited, queue);
                        }
                    }
                }
            }

            // 3.3 PHP 8 Attributes: `#[Route('/api/orders')]`, `#[Inject(OrderRepository::class)]`
            var attrMatches = Regex.Matches(content, @"#\[([A-Za-z0-9_]+)(?:\(([^\]]+)\))?\]");
            foreach (Match am in attrMatches)
            {
                string attrName = am.Groups[1].Value;
                var localFiles = Directory.GetFiles(workspaceRoot, $"{attrName}.php", SearchOption.AllDirectories);
                foreach (var lf in localFiles)
                {
                    AddEdgeAndQueue(fragment, fileNodeId, lf, RelationshipType.Attribute, workspaceRoot, visited, queue);
                }

                if (am.Groups[2].Success)
                {
                    var classRefs = Regex.Matches(am.Groups[2].Value, @"([A-Za-z0-9_]+)::class");
                    foreach (Match cr in classRefs)
                    {
                        string target = cr.Groups[1].Value;
                        var targetFiles = Directory.GetFiles(workspaceRoot, $"{target}.php", SearchOption.AllDirectories);
                        foreach (var tf in targetFiles)
                        {
                            AddEdgeAndQueue(fragment, fileNodeId, tf, RelationshipType.Attribute, workspaceRoot, visited, queue);
                        }
                    }
                }
            }

            // 3.4 Blade View Linking: `view('orders.checkout')` -> `resources/views/orders/checkout.blade.php`
            var viewMatches = Regex.Matches(content, @"view\s*\(\s*['""]([A-Za-z0-9_.-]+)['""]");
            foreach (Match vm in viewMatches)
            {
                string viewName = vm.Groups[1].Value.Replace('.', '/');
                var bladeCandidates = Directory.GetFiles(workspaceRoot, "*.blade.php", SearchOption.AllDirectories);
                foreach (var bc in bladeCandidates)
                {
                    string bcNorm = bc.Replace('\\', '/');
                    if (bcNorm.EndsWith($"{viewName}.blade.php", StringComparison.OrdinalIgnoreCase))
                    {
                        AddEdgeAndQueue(fragment, fileNodeId, bc, RelationshipType.Template, workspaceRoot, visited, queue);
                    }
                }
            }
        }

        return fragment;
    }

    // ==========================================
    // 4. DART / FLUTTER ANALYZER (.dart)
    // ==========================================
    public static DependencyGraphFragment AnalyzeDart(string rootPath, string workspaceRoot)
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
            string curDir = Path.GetDirectoryName(curFile) ?? workspaceRoot;

            fragment.Nodes.Add(new GraphNode
            {
                Id = fileNodeId,
                Kind = NodeKind.File,
                Language = "Dart",
                AnalysisLevel = CapabilityLevel.SyntaxAware,
                DisplayName = Path.GetFileName(curFile),
                QualifiedName = rel,
                RelativePath = rel,
                SourceAvailable = true,
                Content = content
            });

            // 4.1 Imports: `import 'order.dart';`, `import 'package:app/models/order.dart';`
            var importMatches = Regex.Matches(content, @"^\s*import\s+['""]([^'""]+)['""]", RegexOptions.Multiline);
            foreach (Match m in importMatches)
            {
                string importPath = m.Groups[1].Value;
                if (importPath.StartsWith("dart:")) continue;

                if (importPath.StartsWith("package:"))
                {
                    string filename = Path.GetFileName(importPath);
                    var localFiles = Directory.GetFiles(workspaceRoot, filename, SearchOption.AllDirectories);
                    foreach (var lf in localFiles)
                    {
                        AddEdgeAndQueue(fragment, fileNodeId, lf, RelationshipType.Import, workspaceRoot, visited, queue);
                    }
                }
                else
                {
                    string resolved = Path.GetFullPath(Path.Combine(curDir, importPath));
                    if (File.Exists(resolved))
                    {
                        AddEdgeAndQueue(fragment, fileNodeId, resolved, RelationshipType.Import, workspaceRoot, visited, queue);
                    }
                }
            }

            // 4.2 `part 'order.g.dart';` and `part of 'order.dart';`
            var partMatches = Regex.Matches(content, @"^\s*part\s+['""]([^'""]+)['""]\s*;", RegexOptions.Multiline);
            foreach (Match pm in partMatches)
            {
                string partPath = pm.Groups[1].Value;
                string resolved = Path.GetFullPath(Path.Combine(curDir, partPath));
                if (File.Exists(resolved))
                {
                    AddEdgeAndQueue(fragment, fileNodeId, resolved, RelationshipType.CodeBehind, workspaceRoot, visited, queue);
                }
            }

            var partOfMatches = Regex.Matches(content, @"^\s*part\s+of\s+['""]?([^'"";]+)['""]?\s*;", RegexOptions.Multiline);
            foreach (Match pom in partOfMatches)
            {
                string parentName = pom.Groups[1].Value;
                string candidate = Path.GetFullPath(Path.Combine(curDir, parentName.EndsWith(".dart") ? parentName : $"{parentName}.dart"));
                if (File.Exists(candidate))
                {
                    AddEdgeAndQueue(fragment, fileNodeId, candidate, RelationshipType.CodeBehind, workspaceRoot, visited, queue);
                }
            }

            // 4.3 Widget inheritance & mixins: `class OrderCard extends StatelessWidget`, `with DiagnosticableTreeMixin`
            var classMatches = Regex.Matches(content, @"class\s+([A-Za-z0-9_]+)(?:\s+extends\s+([A-Za-z0-9_]+))?(?:\s+with\s+([A-Za-z0-9_,\s]+))?(?:\s+implements\s+([A-Za-z0-9_,\s]+))?");
            foreach (Match cm in classMatches)
            {
                if (cm.Groups[2].Success)
                {
                    string baseClass = cm.Groups[2].Value;
                    if (baseClass is not "StatelessWidget" and not "StatefulWidget" and not "State" and not "Object")
                    {
                        var files = Directory.GetFiles(workspaceRoot, $"{baseClass.ToLowerInvariant()}.dart", SearchOption.AllDirectories);
                        foreach (var f in files)
                        {
                            AddEdgeAndQueue(fragment, fileNodeId, f, RelationshipType.BaseType, workspaceRoot, visited, queue);
                        }
                    }
                }
            }

            // 4.4 Bloc/Riverpod event triggers: `on<CreateOrderEvent>`
            var eventMatches = Regex.Matches(content, @"on<([A-Za-z0-9_]+)>");
            foreach (Match em in eventMatches)
            {
                string eventName = em.Groups[1].Value;
                var files = Directory.GetFiles(workspaceRoot, "*.dart", SearchOption.AllDirectories);
                foreach (var f in files)
                {
                    if (Path.GetFileNameWithoutExtension(f).Equals(eventName, StringComparison.OrdinalIgnoreCase) ||
                        File.ReadAllText(f).Contains($"class {eventName}"))
                    {
                        AddEdgeAndQueue(fragment, fileNodeId, f, RelationshipType.EventType, workspaceRoot, visited, queue);
                    }
                }
            }
        }

        return fragment;
    }

    // ==========================================
    // 5. C / C++ ANALYZER (.c, .cpp, .cc, .cxx, .h, .hpp)
    // ==========================================
    public static DependencyGraphFragment AnalyzeCpp(string rootPath, string workspaceRoot)
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
            string curDir = Path.GetDirectoryName(curFile) ?? workspaceRoot;
            string curExt = Path.GetExtension(curFile).ToLowerInvariant();
            string baseName = Path.GetFileNameWithoutExtension(curFile);

            fragment.Nodes.Add(new GraphNode
            {
                Id = fileNodeId,
                Kind = NodeKind.File,
                Language = curExt is ".c" or ".h" ? "C" : "Cpp",
                AnalysisLevel = CapabilityLevel.SyntaxAware,
                DisplayName = Path.GetFileName(curFile),
                QualifiedName = rel,
                RelativePath = rel,
                SourceAvailable = true,
                Content = content
            });

            // 5.1 Auto Companion Link: .cpp <-> .h / .hpp
            if (curExt is ".cpp" or ".cc" or ".cxx" or ".c")
            {
                string[] headerExtensions = new[] { ".h", ".hpp", ".hxx" };
                foreach (var hext in headerExtensions)
                {
                    string candHeader = Path.Combine(curDir, $"{baseName}{hext}");
                    if (File.Exists(candHeader))
                    {
                        AddEdgeAndQueue(fragment, fileNodeId, candHeader, RelationshipType.CodeBehind, workspaceRoot, visited, queue);
                        break;
                    }
                }
            }
            else if (curExt is ".h" or ".hpp" or ".hxx")
            {
                string[] srcExtensions = new[] { ".cpp", ".cc", ".cxx", ".c" };
                foreach (var sext in srcExtensions)
                {
                    string candSrc = Path.Combine(curDir, $"{baseName}{sext}");
                    if (File.Exists(candSrc))
                    {
                        AddEdgeAndQueue(fragment, fileNodeId, candSrc, RelationshipType.CodeBehind, workspaceRoot, visited, queue);
                        break;
                    }
                }
            }

            // 5.2 `#include "..."` Resolution
            var includeMatches = Regex.Matches(content, @"^\s*#include\s+""([^""]+)""", RegexOptions.Multiline);
            foreach (Match im in includeMatches)
            {
                string includeRel = im.Groups[1].Value;
                string directPath = Path.GetFullPath(Path.Combine(curDir, includeRel));
                if (File.Exists(directPath))
                {
                    AddEdgeAndQueue(fragment, fileNodeId, directPath, RelationshipType.Import, workspaceRoot, visited, queue);
                }
                else
                {
                    string incFileName = Path.GetFileName(includeRel);
                    var matchedFiles = Directory.GetFiles(workspaceRoot, incFileName, SearchOption.AllDirectories);
                    foreach (var mf in matchedFiles)
                    {
                        AddEdgeAndQueue(fragment, fileNodeId, mf, RelationshipType.Import, workspaceRoot, visited, queue);
                    }
                }
            }

            // 5.3 Class / Struct Inheritance: `class OrderService : public IOrderService`
            var inheritanceMatches = Regex.Matches(content, @"(?:class|struct)\s+([A-Za-z0-9_]+)\s*:\s*(?:public|protected|private)?\s*([A-Za-z0-9_]+)");
            foreach (Match inhm in inheritanceMatches)
            {
                string baseNameRef = inhm.Groups[2].Value;
                var matched = Directory.GetFiles(workspaceRoot, $"{baseNameRef}.*", SearchOption.AllDirectories);
                foreach (var mf in matched)
                {
                    string mfExt = Path.GetExtension(mf).ToLowerInvariant();
                    if (mfExt is ".h" or ".hpp" or ".hxx" or ".cpp" or ".cc" or ".c")
                    {
                        AddEdgeAndQueue(fragment, fileNodeId, mf, RelationshipType.BaseType, workspaceRoot, visited, queue);
                    }
                }
            }
        }

        return fragment;
    }

    private static void AddEdgeAndQueue(
        DependencyGraphFragment fragment,
        string sourceNodeId,
        string targetFilePath,
        RelationshipType relationship,
        string workspaceRoot,
        HashSet<string> visited,
        Queue<string> queue)
    {
        string targetRel = Path.GetRelativePath(workspaceRoot, targetFilePath).Replace('\\', '/');
        string targetNodeId = $"file:{targetRel}";

        if (!fragment.Edges.Any(e => e.SourceNodeId == sourceNodeId && e.TargetNodeId == targetNodeId))
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

        if (visited.Add(targetFilePath))
        {
            queue.Enqueue(targetFilePath);
        }
    }
}

// ==========================================
// ADAPTER IMPLEMENTATIONS
// ==========================================

public class RustAdapter : ILanguageAdapter
{
    public string Id => "RustAdapter";
    public CapabilityLevel Capability => CapabilityLevel.SyntaxAware;
    public IReadOnlyList<string> SupportedExtensions => new[] { ".rs" };

    public bool CanHandle(string language, string extension) =>
        extension.Equals(".rs", StringComparison.OrdinalIgnoreCase) || language.Equals("Rust", StringComparison.OrdinalIgnoreCase);

    public Task<DependencyGraphFragment> AnalyzeAsync(string filePath, string workspaceRoot, UserConfiguration config, CancellationToken cancellationToken = default) =>
        Task.FromResult(ExtendedLanguageAnalyzers.AnalyzeRust(filePath, workspaceRoot));
}

public class KotlinAdapter : ILanguageAdapter
{
    public string Id => "KotlinAdapter";
    public CapabilityLevel Capability => CapabilityLevel.SyntaxAware;
    public IReadOnlyList<string> SupportedExtensions => new[] { ".kt", ".kts" };

    public bool CanHandle(string language, string extension) =>
        extension is ".kt" or ".kts" || language.Equals("Kotlin", StringComparison.OrdinalIgnoreCase);

    public Task<DependencyGraphFragment> AnalyzeAsync(string filePath, string workspaceRoot, UserConfiguration config, CancellationToken cancellationToken = default) =>
        Task.FromResult(ExtendedLanguageAnalyzers.AnalyzeKotlin(filePath, workspaceRoot));
}

public class PhpAdapter : ILanguageAdapter
{
    public string Id => "PhpAdapter";
    public CapabilityLevel Capability => CapabilityLevel.SyntaxAware;
    public IReadOnlyList<string> SupportedExtensions => new[] { ".php" };

    public bool CanHandle(string language, string extension) =>
        extension.Equals(".php", StringComparison.OrdinalIgnoreCase) || language.Equals("Php", StringComparison.OrdinalIgnoreCase);

    public Task<DependencyGraphFragment> AnalyzeAsync(string filePath, string workspaceRoot, UserConfiguration config, CancellationToken cancellationToken = default) =>
        Task.FromResult(ExtendedLanguageAnalyzers.AnalyzePhp(filePath, workspaceRoot));
}

public class DartAdapter : ILanguageAdapter
{
    public string Id => "DartAdapter";
    public CapabilityLevel Capability => CapabilityLevel.SyntaxAware;
    public IReadOnlyList<string> SupportedExtensions => new[] { ".dart" };

    public bool CanHandle(string language, string extension) =>
        extension.Equals(".dart", StringComparison.OrdinalIgnoreCase) || language.Equals("Dart", StringComparison.OrdinalIgnoreCase);

    public Task<DependencyGraphFragment> AnalyzeAsync(string filePath, string workspaceRoot, UserConfiguration config, CancellationToken cancellationToken = default) =>
        Task.FromResult(ExtendedLanguageAnalyzers.AnalyzeDart(filePath, workspaceRoot));
}

public class CppAdapter : ILanguageAdapter
{
    public string Id => "CppAdapter";
    public CapabilityLevel Capability => CapabilityLevel.SyntaxAware;
    public IReadOnlyList<string> SupportedExtensions => new[] { ".cpp", ".cc", ".cxx", ".c", ".h", ".hpp", ".hxx" };

    public bool CanHandle(string language, string extension) =>
        SupportedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase) ||
        language is "Cpp" or "C";

    public Task<DependencyGraphFragment> AnalyzeAsync(string filePath, string workspaceRoot, UserConfiguration config, CancellationToken cancellationToken = default) =>
        Task.FromResult(ExtendedLanguageAnalyzers.AnalyzeCpp(filePath, workspaceRoot));
}
