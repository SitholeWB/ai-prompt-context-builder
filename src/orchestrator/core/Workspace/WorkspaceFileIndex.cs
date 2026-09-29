using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AiPromptContextBuilder.Core.Workspace;

/// <summary>
/// Fast, pruned in-memory workspace file index.
/// Strictly prunes build output, package-manager stores (e.g. node_modules, vendor, .git),
/// and caches workspace paths to provide O(1) file lookups across all language analyzers.
/// </summary>
public class WorkspaceFileIndex
{
    private static readonly HashSet<string> IgnoredDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", ".git", ".vs", ".idea", ".vscode", "bin", "obj",
        "dist", "build", "out", "target", "vendor", ".next", ".nuxt", ".turbo",
        "coverage", ".dart_tool", ".gradle", "CMakeFiles"
    };

    private readonly string _workspaceRoot;
    private readonly List<string> _allRelativeFiles = new();
    private readonly Dictionary<string, List<string>> _byExactFileName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<string>> _byBaseName = new(StringComparer.OrdinalIgnoreCase);

    public WorkspaceFileIndex(string workspaceRoot)
    {
        _workspaceRoot = Path.GetFullPath(workspaceRoot);
        BuildIndex();
    }

    public string WorkspaceRoot => _workspaceRoot;
    public IReadOnlyList<string> AllRelativeFiles => _allRelativeFiles;

    private void BuildIndex()
    {
        if (!Directory.Exists(_workspaceRoot)) return;

        var stack = new Stack<string>();
        stack.Push(_workspaceRoot);

        while (stack.Count > 0)
        {
            string currentDir = stack.Pop();
            string[] subDirs;
            string[] files;

            try
            {
                subDirs = Directory.GetDirectories(currentDir);
                files = Directory.GetFiles(currentDir);
            }
            catch
            {
                continue; // Skip inaccessible directories
            }

            foreach (var file in files)
            {
                string rel = Path.GetRelativePath(_workspaceRoot, file).Replace('\\', '/');
                string fileName = Path.GetFileName(file);
                string baseName = Path.GetFileNameWithoutExtension(file);

                _allRelativeFiles.Add(rel);

                if (!_byExactFileName.TryGetValue(fileName, out var exactList))
                {
                    exactList = new List<string>();
                    _byExactFileName[fileName] = exactList;
                }
                exactList.Add(file);

                if (!_byBaseName.TryGetValue(baseName, out var baseList))
                {
                    baseList = new List<string>();
                    _byBaseName[baseName] = baseList;
                }
                baseList.Add(file);
            }

            foreach (var subDir in subDirs)
            {
                string dirName = Path.GetFileName(subDir);
                if (IgnoredDirectoryNames.Contains(dirName))
                {
                    continue; // PRUNED! Never traverse node_modules, .git, bin, obj, target, vendor
                }
                stack.Push(subDir);
            }
        }
    }

    public IReadOnlyList<string> FindFilesByName(string fileName)
    {
        return _byExactFileName.TryGetValue(fileName, out var list) ? list : Array.Empty<string>();
    }

    public IReadOnlyList<string> FindFilesByBaseName(string baseName, params string[] allowedExtensions)
    {
        if (!_byBaseName.TryGetValue(baseName, out var list)) return Array.Empty<string>();
        if (allowedExtensions == null || allowedExtensions.Length == 0) return list;

        return list.Where(f =>
        {
            string ext = Path.GetExtension(f).ToLowerInvariant();
            return allowedExtensions.Any(ae => ae.Equals(ext, StringComparison.OrdinalIgnoreCase));
        }).ToList();
    }

    public IReadOnlyList<string> FindMatchingFiles(Func<string, bool> predicate)
    {
        var result = new List<string>();
        foreach (var rel in _allRelativeFiles)
        {
            if (predicate(rel))
            {
                result.Add(Path.Combine(_workspaceRoot, rel));
            }
        }
        return result;
    }
}
