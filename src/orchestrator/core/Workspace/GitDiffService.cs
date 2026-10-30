using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using AiPromptContextBuilder.Core.Protocol;

namespace AiPromptContextBuilder.Core.Workspace;

public class GitChangesResult
{
    public bool IsGitRepository { get; set; }
    public string GitRoot { get; set; } = string.Empty;
    public List<string> ModifiedAbsolutePaths { get; set; } = new();
    public List<string> UntrackedAbsolutePaths { get; set; } = new();
    public string UnifiedDiff { get; set; } = string.Empty;
    public string ScopeDescription { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
}

public static class GitDiffService
{
    private static readonly HashSet<string> SupportedSourceExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".vb", ".ts", ".tsx", ".js", ".jsx", ".mjs", ".cjs",
        ".py", ".java", ".go", ".rs", ".kt", ".kts", ".php", ".dart",
        ".c", ".cpp", ".cc", ".cxx", ".h", ".hpp", ".hh",
        ".vue", ".razor", ".cshtml", ".html", ".htm", ".css", ".scss", ".sass"
    };

    private static readonly string[] IgnoredDirSegments =
    {
        "node_modules", "bin", "obj", ".git", ".vs", ".idea", ".vscode",
        "dist", "build", "out", "target", "vendor", ".next", ".nuxt", "coverage"
    };

    public static string? FindGitRoot(string startPath)
    {
        if (string.IsNullOrWhiteSpace(startPath)) return null;

        string? current = File.Exists(startPath)
            ? Path.GetDirectoryName(Path.GetFullPath(startPath))
            : Path.GetFullPath(startPath);

        while (!string.IsNullOrEmpty(current))
        {
            var gitPath = Path.Combine(current, ".git");
            if (Directory.Exists(gitPath) || File.Exists(gitPath))
            {
                return current;
            }

            var parent = Directory.GetParent(current);
            if (parent == null || parent.FullName == current) break;
            current = parent.FullName;
        }

        return null;
    }

    public static bool IsSupportedSourceFile(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        if (string.IsNullOrEmpty(fileName)) return false;
        if (fileName.EndsWith("-context.md", StringComparison.OrdinalIgnoreCase)) return false;

        var ext = Path.GetExtension(filePath);
        if (!SupportedSourceExtensions.Contains(ext)) return false;

        var normalized = filePath.Replace('\\', '/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        foreach (var seg in segments)
        {
            if (IgnoredDirSegments.Contains(seg, StringComparer.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    public static GitChangesResult ResolveGitChanges(
        string workspaceOrFilePath,
        GitScopeMode scope,
        string? targetFilePath = null,
        string? compareTarget = null,
        int? commitCount = null)
    {
        var result = new GitChangesResult
        {
            ScopeDescription = GetScopeDescription(scope, compareTarget, commitCount)
        };

        var gitRoot = FindGitRoot(targetFilePath ?? workspaceOrFilePath);
        if (string.IsNullOrEmpty(gitRoot))
        {
            result.IsGitRepository = false;
            result.ErrorMessage = $"No Git repository (.git) found at or above '{workspaceOrFilePath}'.";
            return result;
        }

        result.IsGitRepository = true;
        result.GitRoot = gitRoot;

        bool hasHead = RunGit(gitRoot, "rev-parse --verify HEAD").ExitCode == 0;

        if (scope == GitScopeMode.SingleFile)
        {
            var singleFile = Path.GetFullPath(targetFilePath ?? workspaceOrFilePath);
            if (!File.Exists(singleFile))
            {
                result.ErrorMessage = $"Target file '{singleFile}' does not exist.";
                return result;
            }

            var relFile = Path.GetRelativePath(gitRoot, singleFile).Replace('\\', '/');
            var diffText = new StringBuilder();

            if (hasHead)
            {
                var diffRes = RunGit(gitRoot, $"diff HEAD -- \"{relFile}\"");
                if (!string.IsNullOrWhiteSpace(diffRes.StdOut))
                    diffText.AppendLine(diffRes.StdOut.TrimEnd());
            }
            else
            {
                var stagedRes = RunGit(gitRoot, $"diff --cached -- \"{relFile}\"");
                var unstagedRes = RunGit(gitRoot, $"diff -- \"{relFile}\"");
                if (!string.IsNullOrWhiteSpace(stagedRes.StdOut))
                    diffText.AppendLine(stagedRes.StdOut.TrimEnd());
                if (!string.IsNullOrWhiteSpace(unstagedRes.StdOut))
                    diffText.AppendLine(unstagedRes.StdOut.TrimEnd());
            }

            // Check if untracked
            var untrackedRes = RunGit(gitRoot, $"ls-files --others --exclude-standard -- \"{relFile}\"");
            if (!string.IsNullOrWhiteSpace(untrackedRes.StdOut))
            {
                result.UntrackedAbsolutePaths.Add(singleFile);
                if (diffText.Length == 0)
                {
                    diffText.AppendLine(BuildUntrackedFileDiff(gitRoot, singleFile, relFile));
                }
            }

            result.ModifiedAbsolutePaths.Add(singleFile);
            result.UnifiedDiff = diffText.ToString().Trim();
            return result;
        }

        var modifiedRelPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var untrackedRelPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (scope == GitScopeMode.StagedOnly)
        {
            var namesRes = RunGit(gitRoot, "diff --cached --name-only --diff-filter=ACMRTUXB");
            AddLinesToSet(namesRes.StdOut, modifiedRelPaths);
        }
        else if (scope == GitScopeMode.UnstagedOnly)
        {
            var namesRes = RunGit(gitRoot, "diff --name-only --diff-filter=ACMRTUXB");
            AddLinesToSet(namesRes.StdOut, modifiedRelPaths);
        }
        else if (scope == GitScopeMode.AllChanges)
        {
            if (hasHead)
            {
                var namesRes = RunGit(gitRoot, "diff HEAD --name-only --diff-filter=ACMRTUXB");
                AddLinesToSet(namesRes.StdOut, modifiedRelPaths);
            }
            else
            {
                var stagedNames = RunGit(gitRoot, "diff --cached --name-only --diff-filter=ACMRTUXB");
                var unstagedNames = RunGit(gitRoot, "diff --name-only --diff-filter=ACMRTUXB");
                AddLinesToSet(stagedNames.StdOut, modifiedRelPaths);
                AddLinesToSet(unstagedNames.StdOut, modifiedRelPaths);
            }

            var untrackedRes = RunGit(gitRoot, "ls-files --others --exclude-standard");
            AddLinesToSet(untrackedRes.StdOut, untrackedRelPaths);
            foreach (var u in untrackedRelPaths)
            {
                modifiedRelPaths.Add(u);
            }
        }
        else if (scope == GitScopeMode.CompareBranch)
        {
            string baseBranch = !string.IsNullOrWhiteSpace(compareTarget) ? compareTarget.Trim() : "main";
            var namesRes = RunGit(gitRoot, $"diff \"{baseBranch}...HEAD\" --name-only --diff-filter=ACMRTUXB");
            if (namesRes.ExitCode != 0)
            {
                namesRes = RunGit(gitRoot, $"diff \"{baseBranch}..HEAD\" --name-only --diff-filter=ACMRTUXB");
            }
            AddLinesToSet(namesRes.StdOut, modifiedRelPaths);
        }
        else if (scope == GitScopeMode.LastCommits)
        {
            int count = Math.Max(1, commitCount ?? 1);
            var namesRes = RunGit(gitRoot, $"diff HEAD~{count}..HEAD --name-only --diff-filter=ACMRTUXB");
            if (namesRes.ExitCode != 0)
            {
                // Fallback when repository has fewer than N+1 commits (includes root commit)
                var logNames = RunGit(gitRoot, $"log -n {count} --name-only --pretty=format:\"\" --diff-filter=ACMRTUXB");
                AddLinesToSet(logNames.StdOut, modifiedRelPaths);
            }
            else
            {
                AddLinesToSet(namesRes.StdOut, modifiedRelPaths);
            }
        }

        var validRelPaths = new List<string>();
        foreach (var rel in modifiedRelPaths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            var abs = Path.GetFullPath(Path.Combine(gitRoot, rel));
            if (File.Exists(abs) && IsSupportedSourceFile(abs))
            {
                result.ModifiedAbsolutePaths.Add(abs);
                validRelPaths.Add(rel);
                if (untrackedRelPaths.Contains(rel))
                {
                    result.UntrackedAbsolutePaths.Add(abs);
                }
            }
        }

        if (result.ModifiedAbsolutePaths.Count == 0)
        {
            return result;
        }

        // Collect unified diff for the valid source files
        var combinedDiff = new StringBuilder();
        var trackedRelPaths = validRelPaths.Where(r => !untrackedRelPaths.Contains(r)).ToList();

        if (trackedRelPaths.Count > 0)
        {
            var pathArgs = string.Join(" ", trackedRelPaths.Select(p => $"\"{p}\""));
            if (scope == GitScopeMode.StagedOnly)
            {
                var diffRes = RunGit(gitRoot, $"diff --cached -- {pathArgs}");
                if (!string.IsNullOrWhiteSpace(diffRes.StdOut))
                    combinedDiff.AppendLine(diffRes.StdOut.TrimEnd());
            }
            else if (scope == GitScopeMode.UnstagedOnly)
            {
                var diffRes = RunGit(gitRoot, $"diff -- {pathArgs}");
                if (!string.IsNullOrWhiteSpace(diffRes.StdOut))
                    combinedDiff.AppendLine(diffRes.StdOut.TrimEnd());
            }
            else if (scope == GitScopeMode.AllChanges)
            {
                if (hasHead)
                {
                    var diffRes = RunGit(gitRoot, $"diff HEAD -- {pathArgs}");
                    if (!string.IsNullOrWhiteSpace(diffRes.StdOut))
                        combinedDiff.AppendLine(diffRes.StdOut.TrimEnd());
                }
                else
                {
                    var stagedDiff = RunGit(gitRoot, $"diff --cached -- {pathArgs}");
                    var unstagedDiff = RunGit(gitRoot, $"diff -- {pathArgs}");
                    if (!string.IsNullOrWhiteSpace(stagedDiff.StdOut))
                        combinedDiff.AppendLine(stagedDiff.StdOut.TrimEnd());
                    if (!string.IsNullOrWhiteSpace(unstagedDiff.StdOut))
                        combinedDiff.AppendLine(unstagedDiff.StdOut.TrimEnd());
                }
            }
            else if (scope == GitScopeMode.CompareBranch)
            {
                string baseBranch = !string.IsNullOrWhiteSpace(compareTarget) ? compareTarget.Trim() : "main";
                var logRes = RunGit(gitRoot, $"log --oneline \"{baseBranch}..HEAD\"");
                if (logRes.ExitCode == 0 && !string.IsNullOrWhiteSpace(logRes.StdOut))
                {
                    combinedDiff.AppendLine($"# Commits in branch compared to {baseBranch}:");
                    foreach (var line in logRes.StdOut.TrimEnd().Split('\n'))
                        combinedDiff.AppendLine($"#   {line.TrimEnd('\r')}");
                    combinedDiff.AppendLine();
                }

                var diffRes = RunGit(gitRoot, $"diff \"{baseBranch}...HEAD\" -- {pathArgs}");
                if (diffRes.ExitCode != 0)
                {
                    diffRes = RunGit(gitRoot, $"diff \"{baseBranch}..HEAD\" -- {pathArgs}");
                }
                if (!string.IsNullOrWhiteSpace(diffRes.StdOut))
                    combinedDiff.AppendLine(diffRes.StdOut.TrimEnd());
            }
            else if (scope == GitScopeMode.LastCommits)
            {
                int count = Math.Max(1, commitCount ?? 1);
                var logRes = RunGit(gitRoot, $"log -n {count} --oneline");
                if (logRes.ExitCode == 0 && !string.IsNullOrWhiteSpace(logRes.StdOut))
                {
                    combinedDiff.AppendLine($"# Last {count} commit(s):");
                    foreach (var line in logRes.StdOut.TrimEnd().Split('\n'))
                        combinedDiff.AppendLine($"#   {line.TrimEnd('\r')}");
                    combinedDiff.AppendLine();
                }

                var diffRes = RunGit(gitRoot, $"diff HEAD~{count}..HEAD -- {pathArgs}");
                if (diffRes.ExitCode != 0)
                {
                    diffRes = RunGit(gitRoot, $"log -n {count} -p --pretty=format:\"\" -- {pathArgs}");
                }
                if (!string.IsNullOrWhiteSpace(diffRes.StdOut))
                    combinedDiff.AppendLine(diffRes.StdOut.TrimEnd());
            }
        }

        // For untracked files in AllChanges, synthesize a clean unified diff header
        foreach (var abs in result.UntrackedAbsolutePaths)
        {
            var rel = Path.GetRelativePath(gitRoot, abs).Replace('\\', '/');
            combinedDiff.AppendLine(BuildUntrackedFileDiff(gitRoot, abs, rel));
        }

        result.UnifiedDiff = combinedDiff.ToString().Trim();
        return result;
    }

    public static string GetScopeDescription(GitScopeMode scope, string? compareTarget = null, int? commitCount = null) => scope switch
    {
        GitScopeMode.AllChanges => "All Git Changes (Staged + Unstaged + Untracked)",
        GitScopeMode.StagedOnly => "Staged Git Changes Only",
        GitScopeMode.UnstagedOnly => "Unstaged Working Tree Changes Only",
        GitScopeMode.SingleFile => "Single File Git Changes",
        GitScopeMode.CompareBranch => $"Pull Request / Branch Diff vs '{(!string.IsNullOrWhiteSpace(compareTarget) ? compareTarget : "main")}'",
        GitScopeMode.LastCommits => $"Last {Math.Max(1, commitCount ?? 1)} Commit(s) (HEAD~{Math.Max(1, commitCount ?? 1)}..HEAD)",
        _ => "None"
    };

    private static string BuildUntrackedFileDiff(string gitRoot, string absPath, string relPath)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"diff --git a/{relPath} b/{relPath}");
        sb.AppendLine("new file mode 100644");
        sb.AppendLine("--- /dev/null");
        sb.AppendLine($"+++ b/{relPath}");
        try
        {
            var lines = File.ReadAllLines(absPath);
            sb.AppendLine($"@@ -0,0 +1,{lines.Length} @@");
            foreach (var line in lines)
            {
                sb.AppendLine($"+{line}");
            }
        }
        catch
        {
            sb.AppendLine("@@ -0,0 +1,1 @@");
            sb.AppendLine("+[New untracked file]");
        }
        return sb.ToString().TrimEnd();
    }

    private static void AddLinesToSet(string output, HashSet<string> set)
    {
        if (string.IsNullOrWhiteSpace(output)) return;
        var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var trimmed = line.Trim().Trim('"');
            if (!string.IsNullOrEmpty(trimmed))
            {
                set.Add(trimmed.Replace('\\', '/'));
            }
        }
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

