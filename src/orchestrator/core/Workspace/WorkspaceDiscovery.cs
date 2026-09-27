using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AiContextBuilder.Core.Workspace;

public class DiscoveredWorkspace
{
    public string WorkspaceRoot { get; set; } = string.Empty;
    public string? ProjectRoot { get; set; }
    public List<string> ProjectFiles { get; set; } = new();
    public List<string> RepoIndicators { get; set; } = new();
    public List<string> FrameworkIndicators { get; set; } = new();
    public bool IsMonorepo { get; set; }
    public List<string> PackageRoots { get; set; } = new();
}

public static class WorkspaceDiscovery
{
    private static readonly HashSet<string> RepoRootIndicators = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".sln", ".slnx", "go.work"
    };

    private static readonly HashSet<string> ProjectIndicators = new(StringComparer.OrdinalIgnoreCase)
    {
        "Directory.Build.props",
        "Directory.Packages.props",
        "package.json",
        "package-lock.json",
        "yarn.lock",
        "pnpm-lock.yaml",
        "bun.lock",
        "tsconfig.json",
        "jsconfig.json",
        "angular.json",
        "pom.xml",
        "build.gradle",
        "build.gradle.kts",
        "settings.gradle",
        "settings.gradle.kts",
        "pyproject.toml",
        "setup.py",
        "setup.cfg",
        "requirements.txt",
        "Pipfile",
        "poetry.lock",
        "go.mod"
    };

    private static readonly string[] FrameworkConfigPrefixes = new[]
    {
        "vite.config",
        "webpack.config",
        "vue.config",
        "next.config",
        "nuxt.config",
        "svelte.config",
        "angular.json"
    };

    public static DiscoveredWorkspace Discover(
        string sourceFilePath,
        string? explicitWorkspacePath = null,
        string? explicitProjectPath = null)
    {
        string resolvedSource = Path.GetFullPath(sourceFilePath);
        string currentDir = Directory.Exists(resolvedSource)
            ? resolvedSource
            : (Path.GetDirectoryName(resolvedSource) ?? resolvedSource);

        string boundary = !string.IsNullOrEmpty(explicitWorkspacePath)
            ? Path.GetFullPath(explicitWorkspacePath)
            : Path.GetPathRoot(currentDir) ?? "/";

        var discoveredProjects = new List<string>();
        var discoveredRepo = new List<string>();
        var discoveredFrameworks = new List<string>();
        var packageRoots = new List<string>();
        string? detectedWorkspaceRoot = !string.IsNullOrEmpty(explicitWorkspacePath) ? Path.GetFullPath(explicitWorkspacePath) : null;
        string? detectedProjectRoot = !string.IsNullOrEmpty(explicitProjectPath) ? Path.GetFullPath(explicitProjectPath) : null;

        string dir = currentDir;
        while (true)
        {
            string[] entries = Array.Empty<string>();
            try
            {
                if (Directory.Exists(dir))
                {
                    entries = Directory.GetFileSystemEntries(dir).Select(Path.GetFileName).Where(x => x != null).ToArray()!;
                }
            }
            catch
            {
                // Permission or IO issue
            }

            bool hasRepoIndicator = entries.Any(e => RepoRootIndicators.Contains(e));
            if (hasRepoIndicator && detectedWorkspaceRoot == null)
            {
                detectedWorkspaceRoot = dir;
                discoveredRepo.AddRange(entries.Where(e => RepoRootIndicators.Contains(e)));
            }

            var matchingProjects = entries.Where(e =>
            {
                if (ProjectIndicators.Contains(e)) return true;
                if (e.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
                    e.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase) ||
                    e.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase) ||
                    e.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) ||
                    e.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase))
                    return true;
                return false;
            }).ToList();

            if (matchingProjects.Count > 0)
            {
                detectedProjectRoot ??= dir;
                foreach (var p in matchingProjects)
                {
                    string fullPath = Path.Combine(dir, p);
                    if (!discoveredProjects.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
                    {
                        discoveredProjects.Add(fullPath);
                    }
                }
                if (entries.Contains("package.json", StringComparer.OrdinalIgnoreCase) &&
                    !packageRoots.Contains(dir, StringComparer.OrdinalIgnoreCase))
                {
                    packageRoots.Add(dir);
                }
            }

            var matchingFw = entries.Where(e =>
                FrameworkConfigPrefixes.Any(prefix => e.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)));
            foreach (var f in matchingFw)
            {
                discoveredFrameworks.Add(Path.Combine(dir, f));
            }

            if (string.Equals(dir, boundary, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(dir, Path.GetDirectoryName(dir), StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            string? parent = Path.GetDirectoryName(dir);
            if (string.IsNullOrEmpty(parent)) break;
            dir = parent;
        }

        string finalWorkspaceRoot = detectedWorkspaceRoot ?? detectedProjectRoot ?? currentDir;
        bool isMonorepo = packageRoots.Count > 1 || CheckMonorepoIndicators(finalWorkspaceRoot);

        return new DiscoveredWorkspace
        {
            WorkspaceRoot = finalWorkspaceRoot,
            ProjectRoot = detectedProjectRoot,
            ProjectFiles = discoveredProjects,
            RepoIndicators = discoveredRepo,
            FrameworkIndicators = discoveredFrameworks,
            IsMonorepo = isMonorepo,
            PackageRoots = packageRoots
        };
    }

    private static bool CheckMonorepoIndicators(string dir)
    {
        try
        {
            string pkgJson = Path.Combine(dir, "package.json");
            if (File.Exists(pkgJson))
            {
                string text = File.ReadAllText(pkgJson);
                if (text.Contains("\"workspaces\"")) return true;
            }
            if (File.Exists(Path.Combine(dir, "pnpm-workspace.yaml"))) return true;
            if (File.Exists(Path.Combine(dir, "go.work"))) return true;
            if (File.Exists(Path.Combine(dir, "Directory.Build.props"))) return true;
        }
        catch { }
        return false;
    }
}
