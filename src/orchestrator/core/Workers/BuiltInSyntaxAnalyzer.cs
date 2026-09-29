using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using AiPromptContextBuilder.Core.Protocol;

namespace AiPromptContextBuilder.Core.Workers;

/// <summary>
/// High-speed, in-process syntax-aware analyzer for JavaScript, TypeScript, JSX, TSX, React, Vue, HTML, CSS, and Python.
/// Operates without requiring Node.js or Python to be installed on the host machine.
/// </summary>
public static class BuiltInSyntaxAnalyzer
{
    private static readonly string[] JsExtensions = new[]
    {
        ".tsx", ".ts", ".jsx", ".js", ".mjs", ".cjs", ".vue", ".json", ".css", ".scss", ".sass", ".html"
    };

    public static DependencyGraphFragment AnalyzeJsTs(string rootPath, string workspaceRoot)
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
            string ext = Path.GetExtension(curFile).ToLowerInvariant();

            string lang = ext switch
            {
                ".ts" or ".mts" or ".cts" => "TypeScript",
                ".tsx" => "TSX",
                ".jsx" => "JSX",
                ".html" or ".htm" => "HTML",
                ".css" => "CSS",
                ".scss" => "SCSS",
                ".sass" => "Sass",
                ".vue" => "Vue",
                ".json" => "JSON",
                _ => "JavaScript"
            };

            NodeKind kind = (ext is ".jsx" or ".tsx" or ".vue") ? NodeKind.Component : NodeKind.File;

            fragment.Nodes.Add(new GraphNode
            {
                Id = fileNodeId,
                Kind = kind,
                Language = lang,
                AnalysisLevel = CapabilityLevel.SyntaxAware,
                DisplayName = Path.GetFileName(curFile),
                QualifiedName = rel,
                RelativePath = rel,
                SourceAvailable = true,
                Content = content
            });

            if (ext is ".css" or ".scss" or ".sass")
            {
                AnalyzeStyleImports(curFile, content, fileNodeId, fragment, queue, visited, workspaceRoot);
            }
            else if (ext is ".html" or ".htm")
            {
                AnalyzeHtmlReferences(curFile, content, fileNodeId, fragment, queue, visited, workspaceRoot);
            }
            else
            {
                AnalyzeJsCode(curFile, content, fileNodeId, fragment, queue, visited, workspaceRoot);
            }
        }

        return fragment;
    }

    public static DependencyGraphFragment AnalyzePython(string rootPath, string workspaceRoot)
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
                Language = "Python",
                AnalysisLevel = CapabilityLevel.SyntaxAware,
                DisplayName = Path.GetFileName(curFile),
                QualifiedName = rel,
                RelativePath = rel,
                SourceAvailable = true,
                Content = content
            });

            // 1. from .rel import something or from ..parent import something
            var relFromMatches = Regex.Matches(content, @"^\s*from\s+(\.+[A-Za-z0-9_.]*)\s+import", RegexOptions.Multiline);
            foreach (Match m in relFromMatches)
            {
                string relDots = m.Groups[1].Value;
                string? target = ResolvePythonRelativeImport(curFile, relDots);
                if (target != null && File.Exists(target))
                {
                    LinkAndQueue(fileNodeId, target, RelationshipType.Import, fragment, queue, visited, workspaceRoot);
                }
            }

            // 2. from module import something
            var fromMatches = Regex.Matches(content, @"^\s*from\s+([A-Za-z0-9_]+(?:\.[A-Za-z0-9_]+)*)\s+import", RegexOptions.Multiline);
            foreach (Match m in fromMatches)
            {
                string mod = m.Groups[1].Value;
                string? target = ResolvePythonModule(workspaceRoot, Path.GetDirectoryName(curFile)!, mod);
                if (target != null && File.Exists(target))
                {
                    LinkAndQueue(fileNodeId, target, RelationshipType.Import, fragment, queue, visited, workspaceRoot);
                }
            }

            // 3. import module
            var importMatches = Regex.Matches(content, @"^\s*import\s+([A-Za-z0-9_]+(?:\.[A-Za-z0-9_]+)*)", RegexOptions.Multiline);
            foreach (Match m in importMatches)
            {
                string mod = m.Groups[1].Value;
                string? target = ResolvePythonModule(workspaceRoot, Path.GetDirectoryName(curFile)!, mod);
                if (target != null && File.Exists(target))
                {
                    var relType = mod.Contains("extension", StringComparison.OrdinalIgnoreCase) || mod.Contains("ext", StringComparison.OrdinalIgnoreCase)
                        ? RelationshipType.ExtensionMethod
                        : RelationshipType.Import;
                    LinkAndQueue(fileNodeId, target, relType, fragment, queue, visited, workspaceRoot);
                }
            }

            // 4. Base classes and Mixins: class Customer(CustomerMixin, Base):
            var classMatches = Regex.Matches(content, @"^\s*class\s+[A-Za-z0-9_]+\s*\(([^)]+)\)\s*:", RegexOptions.Multiline);
            foreach (Match cm in classMatches)
            {
                var bases = cm.Groups[1].Value.Split(',');
                foreach (var b in bases)
                {
                    string baseName = b.Trim();
                    if (string.IsNullOrEmpty(baseName)) continue;

                    string? target = ResolvePythonModule(workspaceRoot, Path.GetDirectoryName(curFile)!, baseName);
                    if (target != null && File.Exists(target))
                    {
                        var relType = baseName.EndsWith("Mixin", StringComparison.OrdinalIgnoreCase) || baseName.EndsWith("Extension", StringComparison.OrdinalIgnoreCase)
                            ? RelationshipType.ExtensionMethod
                            : RelationshipType.BaseType;
                        LinkAndQueue(fileNodeId, target, relType, fragment, queue, visited, workspaceRoot);
                    }
                }
            }

            // 5. Local extension and helper modules (extensions.py, helpers.py, utils.py)
            string curDir = Path.GetDirectoryName(curFile)!;
            string[] helperNames = { "extensions.py", "ext.py", "helpers.py", "utils.py", "mixins.py" };
            foreach (var hName in helperNames)
            {
                string hPath = Path.Combine(curDir, hName);
                if (File.Exists(hPath) && !string.Equals(hPath, curFile, StringComparison.OrdinalIgnoreCase))
                {
                    string hContent = File.ReadAllText(hPath);
                    var defMatches = Regex.Matches(hContent, @"^\s*(?:def|class)\s+([A-Za-z0-9_]+)", RegexOptions.Multiline);
                    foreach (Match dm in defMatches)
                    {
                        string fnName = dm.Groups[1].Value;
                        if (Regex.IsMatch(content, $@"\b{Regex.Escape(fnName)}\b"))
                        {
                            LinkAndQueue(fileNodeId, hPath, RelationshipType.ExtensionMethod, fragment, queue, visited, workspaceRoot);
                            break;
                        }
                    }
                }
            }
        }

        return fragment;
    }

    private static void AnalyzeJsCode(
        string curFile,
        string content,
        string fileNodeId,
        DependencyGraphFragment fragment,
        Queue<string> queue,
        HashSet<string> visited,
        string workspaceRoot)
    {
        string dir = Path.GetDirectoryName(curFile)!;

        // 1. Static ES imports & re-exports: import ... from './...' / export ... from './...'
        var esImportMatches = Regex.Matches(content, @"(?:import|from)\s+['""]([^'""]+)['""]");
        foreach (Match m in esImportMatches)
        {
            string spec = m.Groups[1].Value;
            if (IsLocalPath(spec))
            {
                string? resolved = ResolveJsTarget(dir, spec);
                if (resolved != null)
                {
                    var relType = IsStyleFile(resolved) ? RelationshipType.StyleReference : RelationshipType.Import;
                    LinkAndQueue(fileNodeId, resolved, relType, fragment, queue, visited, workspaceRoot);
                }
            }
        }

        // 2. CommonJS require: require('./...')
        var cjsMatches = Regex.Matches(content, @"require\s*\(\s*['""]([^'""]+)['""]\s*\)");
        foreach (Match m in cjsMatches)
        {
            string spec = m.Groups[1].Value;
            if (IsLocalPath(spec))
            {
                string? resolved = ResolveJsTarget(dir, spec);
                if (resolved != null)
                {
                    LinkAndQueue(fileNodeId, resolved, RelationshipType.Import, fragment, queue, visited, workspaceRoot);
                }
            }
        }

        // 3. Dynamic imports: import('./...')
        var dynMatches = Regex.Matches(content, @"import\s*\(\s*['""]([^'""]+)['""]\s*\)");
        foreach (Match m in dynMatches)
        {
            string spec = m.Groups[1].Value;
            if (IsLocalPath(spec))
            {
                string? resolved = ResolveJsTarget(dir, spec);
                if (resolved != null)
                {
                    LinkAndQueue(fileNodeId, resolved, RelationshipType.DynamicImport, fragment, queue, visited, workspaceRoot);
                }
            }
        }

        // 4. React JSX Component Elements: <Component ... /> or <Component>
        var jsxMatches = Regex.Matches(content, @"<([A-Z][A-Za-z0-9_]*)");
        foreach (Match m in jsxMatches)
        {
            string tag = m.Groups[1].Value;
            // Search for sibling or workspace component with this name
            string? sibling = ResolveSiblingComponent(dir, tag);
            if (sibling != null)
            {
                LinkAndQueue(fileNodeId, sibling, RelationshipType.ComponentUsage, fragment, queue, visited, workspaceRoot);
            }
        }

        // 5. Angular Component Decorator: templateUrl & styleUrls
        var tmplMatch = Regex.Match(content, @"templateUrl\s*:\s*['""]([^'""]+)['""]");
        if (tmplMatch.Success)
        {
            string tmplPath = Path.GetFullPath(Path.Combine(dir, tmplMatch.Groups[1].Value));
            if (File.Exists(tmplPath))
            {
                LinkAndQueue(fileNodeId, tmplPath, RelationshipType.Template, fragment, queue, visited, workspaceRoot);
            }
        }

        var styleUrlsMatch = Regex.Match(content, @"styleUrls\s*:\s*\[([\s\S]*?)\]");
        if (styleUrlsMatch.Success)
        {
            var styleMatches = Regex.Matches(styleUrlsMatch.Groups[1].Value, @"['""]([^'""]+\.(?:css|scss|sass))['""]");
            foreach (Match sm in styleMatches)
            {
                string sPath = Path.GetFullPath(Path.Combine(dir, sm.Groups[1].Value));
                if (File.Exists(sPath))
                {
                    LinkAndQueue(fileNodeId, sPath, RelationshipType.Style, fragment, queue, visited, workspaceRoot);
                }
            }
        }

        // 6. Prototype / Module Augmentation Extension Files (*.extensions.ts, *.extensions.js)
        var methodCalls = Regex.Matches(content, @"\.([a-zA-Z0-9_]+)\s*\(");
        if (methodCalls.Count > 0)
        {
            var calledMethodNames = new HashSet<string>(methodCalls.Select(m => m.Groups[1].Value), StringComparer.Ordinal);
            string[] candidateExtFiles;
            try
            {
                candidateExtFiles = Directory.GetFiles(workspaceRoot, "*.*", SearchOption.AllDirectories)
                    .Where(f =>
                    {
                        string name = Path.GetFileName(f).ToLowerInvariant();
                        return (name.Contains("extension") || name.Contains("polyfill")) && (name.EndsWith(".ts") || name.EndsWith(".js"));
                    })
                    .ToArray();
            }
            catch
            {
                candidateExtFiles = Array.Empty<string>();
            }

            foreach (var cef in candidateExtFiles)
            {
                if (string.Equals(cef, curFile, StringComparison.OrdinalIgnoreCase)) continue;

                try
                {
                    string extCode = File.ReadAllText(cef);
                    var protoMatches = Regex.Matches(extCode, @"\.prototype\.([A-Za-z0-9_]+)\s*=");
                    bool matched = false;
                    foreach (Match pm in protoMatches)
                    {
                        if (calledMethodNames.Contains(pm.Groups[1].Value))
                        {
                            LinkAndQueue(fileNodeId, cef, RelationshipType.ExtensionMethod, fragment, queue, visited, workspaceRoot);
                            matched = true;
                            break;
                        }
                    }
                    if (!matched && Regex.IsMatch(extCode, @"declare\s+global"))
                    {
                        var ifaceMethodMatches = Regex.Matches(extCode, @"([a-zA-Z0-9_]+)\s*\([^)]*\)\s*:");
                        foreach (Match im in ifaceMethodMatches)
                        {
                            if (calledMethodNames.Contains(im.Groups[1].Value))
                            {
                                LinkAndQueue(fileNodeId, cef, RelationshipType.ExtensionMethod, fragment, queue, visited, workspaceRoot);
                                break;
                            }
                        }
                    }
                }
                catch { }
            }
        }
    }

    private static void AnalyzeStyleImports(
        string curFile,
        string content,
        string fileNodeId,
        DependencyGraphFragment fragment,
        Queue<string> queue,
        HashSet<string> visited,
        string workspaceRoot)
    {
        string dir = Path.GetDirectoryName(curFile)!;
        var importMatches = Regex.Matches(content, @"@import\s+['""]([^'""]+)['""]");
        foreach (Match m in importMatches)
        {
            string spec = m.Groups[1].Value;
            string? target = ResolveStyleTarget(dir, spec);
            if (target != null)
            {
                LinkAndQueue(fileNodeId, target, RelationshipType.StyleReference, fragment, queue, visited, workspaceRoot);
            }
        }
    }

    private static void AnalyzeHtmlReferences(
        string curFile,
        string content,
        string fileNodeId,
        DependencyGraphFragment fragment,
        Queue<string> queue,
        HashSet<string> visited,
        string workspaceRoot)
    {
        string dir = Path.GetDirectoryName(curFile)!;
        var scriptMatches = Regex.Matches(content, @"<script\s+[^>]*src=['""]([^'""]+)['""]", RegexOptions.IgnoreCase);
        foreach (Match m in scriptMatches)
        {
            string src = m.Groups[1].Value;
            if (IsLocalPath(src))
            {
                string target = Path.GetFullPath(Path.Combine(dir, src));
                if (File.Exists(target))
                {
                    LinkAndQueue(fileNodeId, target, RelationshipType.ScriptReference, fragment, queue, visited, workspaceRoot);
                }
            }
        }

        var linkMatches = Regex.Matches(content, @"<link\s+[^>]*href=['""]([^'""]+)['""]", RegexOptions.IgnoreCase);
        foreach (Match m in linkMatches)
        {
            string href = m.Groups[1].Value;
            if (IsLocalPath(href) && (href.EndsWith(".css", StringComparison.OrdinalIgnoreCase) || href.EndsWith(".scss", StringComparison.OrdinalIgnoreCase)))
            {
                string target = Path.GetFullPath(Path.Combine(dir, href));
                if (File.Exists(target))
                {
                    LinkAndQueue(fileNodeId, target, RelationshipType.StyleReference, fragment, queue, visited, workspaceRoot);
                }
            }
        }
    }

    private static string? ResolveJsTarget(string baseDir, string specifier)
    {
        string raw = Path.GetFullPath(Path.Combine(baseDir, specifier));
        if (File.Exists(raw)) return raw;

        // Try candidate extensions
        foreach (var ext in JsExtensions)
        {
            string candidate = raw + ext;
            if (File.Exists(candidate)) return candidate;
        }

        // Try index files inside directory
        if (Directory.Exists(raw))
        {
            foreach (var ext in JsExtensions)
            {
                string indexCandidate = Path.Combine(raw, "index" + ext);
                if (File.Exists(indexCandidate)) return indexCandidate;
            }
        }

        return null;
    }

    private static string? ResolveSiblingComponent(string dir, string tagName)
    {
        var exts = new[] { ".jsx", ".tsx", ".js", ".ts", ".vue" };
        foreach (var ext in exts)
        {
            string candidate = Path.Combine(dir, tagName + ext);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static string? ResolveStyleTarget(string dir, string specifier)
    {
        string raw = Path.GetFullPath(Path.Combine(dir, specifier));
        if (File.Exists(raw)) return raw;

        var exts = new[] { ".scss", ".sass", ".css" };
        foreach (var ext in exts)
        {
            string candidate = raw + ext;
            if (File.Exists(candidate)) return candidate;

            // SCSS partial with leading underscore: _variables.scss
            string partial = Path.Combine(Path.GetDirectoryName(raw)!, "_" + Path.GetFileName(raw) + ext);
            if (File.Exists(partial)) return partial;
        }
        return null;
    }

    private static string? ResolvePythonRelativeImport(string curFile, string dotsAndModule)
    {
        string dir = Path.GetDirectoryName(curFile)!;
        int dotCount = dotsAndModule.TakeWhile(c => c == '.').Count();
        string modPart = dotsAndModule.Substring(dotCount);

        for (int i = 1; i < dotCount; i++)
        {
            var parent = Directory.GetParent(dir);
            if (parent != null) dir = parent.FullName;
        }

        if (string.IsNullOrEmpty(modPart))
        {
            string init = Path.Combine(dir, "__init__.py");
            if (File.Exists(init)) return init;
            return null;
        }

        string modPath = Path.Combine(dir, modPart.Replace('.', Path.DirectorySeparatorChar));
        string directPy = modPath + ".py";
        if (File.Exists(directPy)) return directPy;

        string initPy = Path.Combine(modPath, "__init__.py");
        if (File.Exists(initPy)) return initPy;

        return null;
    }

    private static string? ResolvePythonModule(string workspaceRoot, string currentDir, string moduleDotted)
    {
        string relPath = moduleDotted.Replace('.', Path.DirectorySeparatorChar);

        // Check relative to current directory first
        string localDirect = Path.Combine(currentDir, relPath + ".py");
        if (File.Exists(localDirect)) return localDirect;

        string localInit = Path.Combine(currentDir, relPath, "__init__.py");
        if (File.Exists(localInit)) return localInit;

        // Check relative to workspace root
        string wsDirect = Path.Combine(workspaceRoot, relPath + ".py");
        if (File.Exists(wsDirect)) return wsDirect;

        string wsInit = Path.Combine(workspaceRoot, relPath, "__init__.py");
        if (File.Exists(wsInit)) return wsInit;

        return null;
    }

    private static bool IsLocalPath(string spec) =>
        spec.StartsWith("./") || spec.StartsWith("../") || spec.StartsWith(".\\") || spec.StartsWith("..\\");

    private static bool IsStyleFile(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".css" or ".scss" or ".sass";
    }

    private static void LinkAndQueue(
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

        if (visited.Add(targetFile))
        {
            queue.Enqueue(targetFile);
        }
    }
}
