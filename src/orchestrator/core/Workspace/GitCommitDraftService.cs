using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using AiPromptContextBuilder.Core.Protocol;

namespace AiPromptContextBuilder.Core.Workspace;

public class FileChangeSummary
{
    public string RelativePath { get; set; } = string.Empty;
    public string ChangeKind { get; set; } = "Modified"; // Added, Modified, Deleted, Renamed
    public int Insertions { get; set; }
    public int Deletions { get; set; }
    public List<string> AddedSymbols { get; set; } = new();
    public List<string> ModifiedSymbols { get; set; } = new();
    public List<string> RemovedSymbols { get; set; } = new();
}

public class CommitDraftResult
{
    public bool Success { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public string CommitType { get; set; } = "feat";
    public string? Scope { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string FullCommitMessage { get; set; } = string.Empty;
    public string ScopeUsed { get; set; } = string.Empty;
    public int FilesChanged { get; set; }
    public int Insertions { get; set; }
    public int Deletions { get; set; }
    public List<FileChangeSummary> FileSummaries { get; set; } = new();
    public string CompactDiffForLlm { get; set; } = string.Empty;
}

public static class GitCommitDraftService
{
    private static readonly Regex SymbolDeclRegex = new(
        @"(?:^\s*(?:public|private|protected|internal|export|async|static|virtual|override|abstract|sealed|partial|default|\s)*\s*(?:class|interface|struct|enum|record|function|def|fn|func|trait|impl|type)\s+([A-Za-z_][A-Za-z0-9_]*))|(?:^\s*(?:public|private|protected|internal|static|async|virtual|override)\s+[A-Za-z0-9_<>,\.\[\]\?\s]+\s+([A-Za-z_][A-Za-z0-9_]*)\s*\()|(?:^\s*(?:export\s+)?(?:const|let|var)\s+([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?:async\s*)?(?:\([^)]*\)|[A-Za-z_][A-Za-z0-9_]*)\s*=>)",
        RegexOptions.Compiled);

    private static readonly Regex HunkHeaderRegex = new(
        @"^@@\s+-\d+(?:,\d+)?\s+\+\d+(?:,\d+)?\s+@@\s*(.*)$",
        RegexOptions.Compiled);

    private static readonly string[] IgnoredNoiseSegments =
    {
        "node_modules", "bin", "obj", ".git", ".vs", ".idea",
        "dist", "build", "out", "target", "vendor", "coverage"
    };

    public static CommitDraftResult DraftCommitMessage(string workspaceOrFilePath, GitScopeMode requestedScope = GitScopeMode.None)
    {
        var gitRoot = GitDiffService.FindGitRoot(workspaceOrFilePath);
        if (string.IsNullOrEmpty(gitRoot))
        {
            return new CommitDraftResult
            {
                Success = false,
                ErrorCode = "WorkspaceNotFound",
                ErrorMessage = $"No Git repository (.git) found at or above '{workspaceOrFilePath}'."
            };
        }

        bool hasHead = RunGit(gitRoot, "rev-parse --verify HEAD").ExitCode == 0;

        // Determine effective scope: if None (Auto), prefer StagedOnly if staged changes exist, else AllChanges
        GitScopeMode effectiveScope = requestedScope;
        if (effectiveScope == GitScopeMode.None)
        {
            var stagedCheck = RunGit(gitRoot, "diff --cached --name-only");
            effectiveScope = !string.IsNullOrWhiteSpace(stagedCheck.StdOut)
                ? GitScopeMode.StagedOnly
                : GitScopeMode.AllChanges;
        }

        string scopeLabel = effectiveScope switch
        {
            GitScopeMode.StagedOnly => "Staged Changes (git diff --cached)",
            GitScopeMode.UnstagedOnly => "Unstaged Changes (git diff)",
            _ => "All Uncommitted Changes (Staged + Unstaged + Untracked)"
        };

        var fileStatusMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (effectiveScope == GitScopeMode.StagedOnly)
        {
            var statusOut = RunGit(gitRoot, "diff --cached --name-status").StdOut;
            ParseNameStatus(statusOut, fileStatusMap);
        }
        else if (effectiveScope == GitScopeMode.UnstagedOnly)
        {
            var statusOut = RunGit(gitRoot, "diff --name-status").StdOut;
            ParseNameStatus(statusOut, fileStatusMap);
        }
        else
        {
            if (hasHead)
            {
                var statusOut = RunGit(gitRoot, "diff HEAD --name-status").StdOut;
                ParseNameStatus(statusOut, fileStatusMap);
            }
            else
            {
                ParseNameStatus(RunGit(gitRoot, "diff --cached --name-status").StdOut, fileStatusMap);
                ParseNameStatus(RunGit(gitRoot, "diff --name-status").StdOut, fileStatusMap);
            }

            var untrackedOut = RunGit(gitRoot, "ls-files --others --exclude-standard").StdOut;
            if (!string.IsNullOrWhiteSpace(untrackedOut))
            {
                foreach (var line in untrackedOut.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var rel = line.Trim().Trim('"').Replace('\\', '/');
                    if (!string.IsNullOrEmpty(rel) && !IsNoisePath(rel))
                    {
                        fileStatusMap.TryAdd(rel, "Added");
                    }
                }
            }
        }

        // Filter out noise directories and generated *-context.md files
        var validPaths = fileStatusMap.Keys
            .Where(p => !IsNoisePath(p))
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (validPaths.Count == 0)
        {
            return new CommitDraftResult
            {
                Success = false,
                ErrorCode = "NoGitChanges",
                ErrorMessage = $"No uncommitted changes found for '{scopeLabel}'."
            };
        }

        var summaries = new List<FileChangeSummary>();
        var compactDiffSb = new StringBuilder();
        int totalInsertions = 0;
        int totalDeletions = 0;
        int fixSignalScore = 0;

        foreach (var relPath in validPaths)
        {
            string changeKind = fileStatusMap[relPath];
            string diffText = GetFileDiff(gitRoot, relPath, effectiveScope, hasHead, changeKind);

            var summary = AnalyzeFileDiff(gitRoot, relPath, changeKind, diffText, out int fileFixSignals);
            fixSignalScore += fileFixSignals;
            totalInsertions += summary.Insertions;
            totalDeletions += summary.Deletions;
            summaries.Add(summary);

            if (compactDiffSb.Length < 12000 && !string.IsNullOrWhiteSpace(diffText))
            {
                string snippet = diffText.Length > 2000 ? diffText.Substring(0, 2000) + "\n... (diff truncated)" : diffText;
                compactDiffSb.AppendLine(snippet);
            }
        }

        // Infer Conventional Commit Type & Scope
        string commitType = InferCommitType(summaries, fixSignalScore);
        string? scope = InferCommitScope(summaries);

        // Build Subject Line
        string subjectAction = BuildSubjectAction(commitType, summaries);
        string prefix = !string.IsNullOrEmpty(scope) ? $"{commitType}({scope}): " : $"{commitType}: ";
        string subject = prefix + subjectAction;
        if (subject.Length > 76)
        {
            subject = subject.Substring(0, 73).TrimEnd(',', ' ', ';') + "...";
        }

        // Build Detailed Bullet-Point Body
        var bodySb = new StringBuilder();
        foreach (var s in summaries)
        {
            string fileName = Path.GetFileName(s.RelativePath);
            string verb = s.ChangeKind switch
            {
                "Added" => "Add",
                "Deleted" => "Remove",
                "Renamed" => "Rename",
                _ => "Update"
            };

            var details = new List<string>();
            if (s.AddedSymbols.Count > 0)
            {
                details.Add($"add {string.Join(", ", s.AddedSymbols.Take(4))}");
            }
            if (s.ModifiedSymbols.Count > 0)
            {
                details.Add($"update {string.Join(", ", s.ModifiedSymbols.Take(4))}");
            }
            if (s.RemovedSymbols.Count > 0)
            {
                details.Add($"remove {string.Join(", ", s.RemovedSymbols.Take(3))}");
            }

            string statTag = (s.Insertions > 0 || s.Deletions > 0) ? $" (+{s.Insertions}/-{s.Deletions})" : "";
            if (details.Count > 0)
            {
                bodySb.AppendLine($"- {verb} {fileName}: {string.Join("; ", details)}{statTag}");
            }
            else
            {
                bodySb.AppendLine($"- {verb} { s.RelativePath }{statTag}");
            }
        }

        string body = bodySb.ToString().TrimEnd();
        string fullMessage = string.IsNullOrEmpty(body) ? subject : $"{subject}\n\n{body}";

        return new CommitDraftResult
        {
            Success = true,
            CommitType = commitType,
            Scope = scope,
            Subject = subject,
            Body = body,
            FullCommitMessage = fullMessage,
            ScopeUsed = scopeLabel,
            FilesChanged = summaries.Count,
            Insertions = totalInsertions,
            Deletions = totalDeletions,
            FileSummaries = summaries,
            CompactDiffForLlm = compactDiffSb.ToString().Trim()
        };
    }

    private static FileChangeSummary AnalyzeFileDiff(
        string gitRoot,
        string relPath,
        string changeKind,
        string diffText,
        out int fixSignals)
    {
        fixSignals = 0;
        var summary = new FileChangeSummary
        {
            RelativePath = relPath,
            ChangeKind = changeKind
        };

        var addedSyms = new HashSet<string>(StringComparer.Ordinal);
        var modifiedSyms = new HashSet<string>(StringComparer.Ordinal);
        var removedSyms = new HashSet<string>(StringComparer.Ordinal);

        if (string.IsNullOrWhiteSpace(diffText))
        {
            // Untracked or binary file: scan file on disk if Added
            string absPath = Path.Combine(gitRoot, relPath);
            if (changeKind == "Added" && File.Exists(absPath))
            {
                try
                {
                    var lines = File.ReadAllLines(absPath);
                    summary.Insertions = lines.Length;
                    foreach (var line in lines)
                    {
                        var sym = TryExtractSymbolName(line);
                        if (sym != null) addedSyms.Add(sym);
                    }
                }
                catch { }
            }
        }
        else
        {
            var lines = diffText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                if (line.StartsWith("+++") || line.StartsWith("---") || line.StartsWith("diff --git") || line.StartsWith("index "))
                    continue;

                var hunkMatch = HunkHeaderRegex.Match(line);
                if (hunkMatch.Success)
                {
                    string hunkCtx = hunkMatch.Groups[1].Value.Trim();
                    var hunkSym = TryExtractSymbolName(hunkCtx);
                    if (hunkSym != null)
                    {
                        modifiedSyms.Add(hunkSym);
                    }
                    continue;
                }

                if (line.StartsWith("+"))
                {
                    summary.Insertions++;
                    string content = line.Substring(1);
                    var sym = TryExtractSymbolName(content);
                    if (sym != null)
                    {
                        addedSyms.Add(sym);
                    }
                    if (Regex.IsMatch(content, @"\b(try|catch|NullReference|ArgumentNullException|fallback|fix|bug)\b|(!==?\s*null|===?\s*null|\?\.)", RegexOptions.IgnoreCase))
                    {
                        fixSignals++;
                    }
                }
                else if (line.StartsWith("-"))
                {
                    summary.Deletions++;
                    string content = line.Substring(1);
                    var sym = TryExtractSymbolName(content);
                    if (sym != null)
                    {
                        removedSyms.Add(sym);
                    }
                }
            }
        }

        // If a symbol appears in both added and removed, it was modified (signature updated)
        foreach (var both in addedSyms.Intersect(removedSyms).ToList())
        {
            addedSyms.Remove(both);
            removedSyms.Remove(both);
            modifiedSyms.Add(both);
        }

        foreach (var a in addedSyms) modifiedSyms.Remove(a);

        summary.AddedSymbols = addedSyms.ToList();
        summary.ModifiedSymbols = modifiedSyms.ToList();
        summary.RemovedSymbols = removedSyms.ToList();
        return summary;
    }

    private static string? TryExtractSymbolName(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        var m = SymbolDeclRegex.Match(line);
        if (!m.Success) return null;

        for (int i = 1; i <= 3; i++)
        {
            if (m.Groups[i].Success && !string.IsNullOrWhiteSpace(m.Groups[i].Value))
            {
                string candidate = m.Groups[i].Value.Trim();
                if (!IsLanguageKeyword(candidate))
                    return candidate;
            }
        }
        return null;
    }

    private static bool IsLanguageKeyword(string word) =>
        word is "if" or "for" or "foreach" or "while" or "switch" or "catch" or "using" or "return" or "new" or "typeof" or "sizeof" or "nameof" or "await" or "lock";

    private static string InferCommitType(List<FileChangeSummary> files, int fixSignals)
    {
        bool allDocs = files.All(f =>
            f.RelativePath.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ||
            f.RelativePath.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ||
            f.RelativePath.StartsWith("docs/", StringComparison.OrdinalIgnoreCase));
        if (allDocs) return "docs";

        bool allTests = files.All(f =>
            f.RelativePath.StartsWith("tests/", StringComparison.OrdinalIgnoreCase) ||
            f.RelativePath.StartsWith("test/", StringComparison.OrdinalIgnoreCase) ||
            f.RelativePath.Contains(".test.", StringComparison.OrdinalIgnoreCase) ||
            f.RelativePath.Contains(".spec.", StringComparison.OrdinalIgnoreCase) ||
            f.RelativePath.EndsWith("Tests.cs", StringComparison.OrdinalIgnoreCase));
        if (allTests) return "test";

        bool allStyles = files.All(f =>
            f.RelativePath.EndsWith(".css", StringComparison.OrdinalIgnoreCase) ||
            f.RelativePath.EndsWith(".scss", StringComparison.OrdinalIgnoreCase) ||
            f.RelativePath.EndsWith(".sass", StringComparison.OrdinalIgnoreCase) ||
            f.RelativePath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase));
        if (allStyles) return "style";

        bool allConfigOrBuild = files.All(f =>
            f.RelativePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
            f.RelativePath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
            f.RelativePath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) ||
            f.RelativePath.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) ||
            f.RelativePath.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase) ||
            f.RelativePath.EndsWith(".toml", StringComparison.OrdinalIgnoreCase));
        if (allConfigOrBuild) return "chore";

        bool anyAddedFile = files.Any(f => f.ChangeKind == "Added");
        int totalAddedSymbols = files.Sum(f => f.AddedSymbols.Count);

        if (anyAddedFile || totalAddedSymbols > 0)
            return "feat";

        if (fixSignals >= 2 && totalAddedSymbols == 0)
            return "fix";

        return "refactor";
    }

    private static string? InferCommitScope(List<FileChangeSummary> files)
    {
        if (files.Count == 1)
        {
            string stem = Path.GetFileNameWithoutExtension(files[0].RelativePath);
            if (!string.IsNullOrEmpty(stem) && stem.Length <= 20 && !stem.Equals("Program", StringComparison.OrdinalIgnoreCase) && !stem.Equals("index", StringComparison.OrdinalIgnoreCase))
            {
                return stem.ToLowerInvariant();
            }
        }

        // Find common meaningful directory segment
        var dirVotes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var ignoredGenericDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "src", "lib", "app", "orchestrator", "extensions", "workers", "components", "services", "controllers", "models"
        };

        foreach (var f in files)
        {
            var segments = f.RelativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < segments.Length - 1; i++)
            {
                string seg = segments[i];
                if (!ignoredGenericDirs.Contains(seg))
                {
                    dirVotes[seg] = dirVotes.GetValueOrDefault(seg, 0) + 1;
                }
            }
        }

        if (dirVotes.Count > 0)
        {
            var top = dirVotes.OrderByDescending(kv => kv.Value).First();
            if (top.Value >= Math.Max(1, files.Count / 2))
            {
                return top.Key.ToLowerInvariant();
            }
        }

        return null;
    }

    private static string BuildSubjectAction(string commitType, List<FileChangeSummary> files)
    {
        var addedFiles = files.Where(f => f.ChangeKind == "Added").Select(f => Path.GetFileNameWithoutExtension(f.RelativePath)).Distinct().ToList();
        var modifiedFiles = files.Where(f => f.ChangeKind == "Modified").Select(f => Path.GetFileNameWithoutExtension(f.RelativePath)).Distinct().ToList();
        var deletedFiles = files.Where(f => f.ChangeKind == "Deleted").Select(f => Path.GetFileNameWithoutExtension(f.RelativePath)).Distinct().ToList();

        var allAddedSymbols = files.SelectMany(f => f.AddedSymbols).Distinct().ToList();
        var allModifiedSymbols = files.SelectMany(f => f.ModifiedSymbols).Distinct().ToList();

        if (files.Count == 1)
        {
            var f = files[0];
            string name = Path.GetFileName(f.RelativePath);
            if (f.ChangeKind == "Added")
            {
                if (f.AddedSymbols.Count > 0)
                    return $"add {name} ({string.Join(", ", f.AddedSymbols.Take(2))})";
                return $"add {name}";
            }
            if (f.ChangeKind == "Deleted")
            {
                return $"remove {name}";
            }
            if (f.AddedSymbols.Count > 0)
            {
                return $"add {string.Join(", ", f.AddedSymbols.Take(2))} in {name}";
            }
            if (f.ModifiedSymbols.Count > 0)
            {
                return $"update {string.Join(", ", f.ModifiedSymbols.Take(2))} in {name}";
            }
            return $"update {name}";
        }

        var parts = new List<string>();
        if (addedFiles.Count > 0)
        {
            parts.Add($"add {string.Join(", ", addedFiles.Take(2))}");
        }
        else if (allAddedSymbols.Count > 0)
        {
            parts.Add($"add {string.Join(", ", allAddedSymbols.Take(2))}");
        }

        if (modifiedFiles.Count > 0)
        {
            parts.Add($"update {string.Join(", ", modifiedFiles.Take(2))}");
        }
        else if (allModifiedSymbols.Count > 0 && parts.Count == 0)
        {
            parts.Add($"update {string.Join(", ", allModifiedSymbols.Take(2))}");
        }

        if (deletedFiles.Count > 0 && parts.Count < 2)
        {
            parts.Add($"remove {string.Join(", ", deletedFiles.Take(2))}");
        }

        if (parts.Count > 0)
        {
            return string.Join(" and ", parts.Take(2));
        }

        return $"update {files.Count} files";
    }

    private static string GetFileDiff(string gitRoot, string relPath, GitScopeMode scope, bool hasHead, string changeKind)
    {
        if (changeKind == "Added" && scope != GitScopeMode.StagedOnly)
        {
            var trackedCheck = RunGit(gitRoot, $"ls-files -- \"{relPath}\"");
            if (string.IsNullOrWhiteSpace(trackedCheck.StdOut))
            {
                return string.Empty;
            }
        }

        if (scope == GitScopeMode.StagedOnly)
        {
            return RunGit(gitRoot, $"diff --cached -- \"{relPath}\"").StdOut;
        }
        if (scope == GitScopeMode.UnstagedOnly)
        {
            return RunGit(gitRoot, $"diff -- \"{relPath}\"").StdOut;
        }
        if (hasHead)
        {
            return RunGit(gitRoot, $"diff HEAD -- \"{relPath}\"").StdOut;
        }
        return RunGit(gitRoot, $"diff --cached -- \"{relPath}\"").StdOut + "\n" + RunGit(gitRoot, $"diff -- \"{relPath}\"").StdOut;
    }

    private static void ParseNameStatus(string output, Dictionary<string, string> map)
    {
        if (string.IsNullOrWhiteSpace(output)) return;
        var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var parts = line.Split('\t', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;

            string statusChar = parts[0].Trim();
            string path = parts[parts.Length - 1].Trim().Trim('"').Replace('\\', '/');

            string kind = statusChar.StartsWith("A", StringComparison.OrdinalIgnoreCase) ? "Added"
                : statusChar.StartsWith("D", StringComparison.OrdinalIgnoreCase) ? "Deleted"
                : statusChar.StartsWith("R", StringComparison.OrdinalIgnoreCase) ? "Renamed"
                : "Modified";

            map[path] = kind;
        }
    }

    private static bool IsNoisePath(string relPath)
    {
        var fileName = Path.GetFileName(relPath);
        if (fileName.EndsWith("-context.md", StringComparison.OrdinalIgnoreCase)) return true;
        if (fileName.EndsWith(".vsix", StringComparison.OrdinalIgnoreCase)) return true;

        var segments = relPath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        foreach (var s in segments)
        {
            if (IgnoredNoiseSegments.Contains(s, StringComparer.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static (int ExitCode, string StdOut, string StdErr) RunGit(string workingDir, string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = arguments,
                WorkingDirectory = workingDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using var process = Process.Start(psi);
            if (process == null)
                return (-1, string.Empty, "Failed to start git process.");

            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit(15000);

            return (process.ExitCode, stdout, stderr);
        }
        catch (Exception ex)
        {
            return (-1, string.Empty, ex.Message);
        }
    }
}

