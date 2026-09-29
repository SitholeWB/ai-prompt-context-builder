using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;

namespace AiPromptContextBuilder.Core.Workers;

public static class WorkerLocator
{
    private static readonly object _extractLock = new();
    private static string? _cachedNodeExe;
    private static string? _cachedPythonExe;
    private static bool _nodeChecked;
    private static bool _pythonChecked;

    /// <summary>
    /// Locates the specified worker script on disk, or extracts it from embedded resources if missing.
    /// </summary>
    public static string LocateWorkerScript(string workerFileName, string relativeDevPath)
    {
        // 1. Direct checks relative to AppContext.BaseDirectory
        string baseDir = AppContext.BaseDirectory;
        string[] localCandidates =
        {
            Path.Combine(baseDir, workerFileName),
            Path.Combine(baseDir, "workers", "node", workerFileName),
            Path.Combine(baseDir, "workers", "python", workerFileName),
            Path.Combine(baseDir, relativeDevPath)
        };

        foreach (var c in localCandidates)
        {
            if (File.Exists(c)) return Path.GetFullPath(c);
        }

        // 2. Search upwards from AppContext.BaseDirectory
        var dir = new DirectoryInfo(baseDir);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, relativeDevPath);
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);

            string flatCandidate = Path.Combine(dir.FullName, workerFileName);
            if (File.Exists(flatCandidate)) return Path.GetFullPath(flatCandidate);

            dir = dir.Parent;
        }

        // 3. Search upwards from Working Directory
        dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, relativeDevPath);
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);

            string flatCandidate = Path.Combine(dir.FullName, workerFileName);
            if (File.Exists(flatCandidate)) return Path.GetFullPath(flatCandidate);

            dir = dir.Parent;
        }

        // 4. Extract from EmbeddedResource to temp directory if not found on disk
        string? extracted = TryExtractEmbeddedResource(workerFileName);
        if (!string.IsNullOrEmpty(extracted) && File.Exists(extracted))
        {
            return extracted;
        }

        // 5. Final fallback to relative dev path
        return Path.GetFullPath(relativeDevPath);
    }

    /// <summary>
    /// Legacy overload for backwards compatibility.
    /// </summary>
    public static string LocateWorkerScript(string relativePath)
    {
        string fileName = Path.GetFileName(relativePath);
        return LocateWorkerScript(fileName, relativePath);
    }

    private static string? TryExtractEmbeddedResource(string resourceFileName)
    {
        lock (_extractLock)
        {
            try
            {
                var assembly = typeof(WorkerLocator).Assembly;
                string? resName = assembly.GetManifestResourceNames()
                    .FirstOrDefault(n => n.Equals(resourceFileName, StringComparison.OrdinalIgnoreCase) ||
                                         n.EndsWith("." + resourceFileName, StringComparison.OrdinalIgnoreCase));

                if (resName == null) return null;

                using var stream = assembly.GetManifestResourceStream(resName);
                if (stream == null) return null;

                string tempDir = Path.Combine(Path.GetTempPath(), "aipromptcontext_workers");
                if (!Directory.Exists(tempDir))
                {
                    Directory.CreateDirectory(tempDir);
                }

                string targetPath = Path.Combine(tempDir, resourceFileName);
                if (!File.Exists(targetPath) || new FileInfo(targetPath).Length != stream.Length)
                {
                    using var fileStream = File.Create(targetPath);
                    stream.CopyTo(fileStream);
                }

                return targetPath;
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Checks whether Node.js is installed and accessible.
    /// </summary>
    public static bool IsNodeAvailable()
    {
        return LocateNodeExecutable() != null;
    }

    /// <summary>
    /// Locates the appropriate Node.js executable, or returns null if not installed.
    /// </summary>
    public static string? LocateNodeExecutable()
    {
        if (_nodeChecked) return _cachedNodeExe;

        _cachedNodeExe = FindNodeExecutable();
        _nodeChecked = true;
        return _cachedNodeExe;
    }

    /// <summary>
    /// Checks whether Python is installed and accessible.
    /// </summary>
    public static bool IsPythonAvailable()
    {
        return LocatePythonExecutable() != null;
    }

    /// <summary>
    /// Locates the appropriate Python executable, or returns null if not installed.
    /// </summary>
    public static string? LocatePythonExecutable()
    {
        if (_pythonChecked) return _cachedPythonExe;

        _cachedPythonExe = FindPythonExecutable();
        _pythonChecked = true;
        return _cachedPythonExe;
    }

    private static string? FindNodeExecutable()
    {
        // 1. Explicit environment override
        string? envPath = Environment.GetEnvironmentVariable("AIPROMPTCONTEXT_NODE_PATH");
        if (!string.IsNullOrEmpty(envPath) && File.Exists(envPath)) return envPath;

        // 2. Windows standard paths
        if (OperatingSystem.IsWindows())
        {
            var winCandidates = new List<string>
            {
                @"C:\Program Files\nodejs\node.exe",
                @"C:\Program Files (x86)\nodejs\node.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "node", "node.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "node.cmd"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "scoop", "apps", "nodejs", "current", "node.exe")
            };

            foreach (var cand in winCandidates)
            {
                if (File.Exists(cand)) return cand;
            }
        }
        else
        {
            // Unix standard paths
            var unixCandidates = new[]
            {
                "/usr/bin/node",
                "/usr/local/bin/node",
                "/opt/homebrew/bin/node",
                "/usr/local/opt/node/bin/node"
            };

            foreach (var cand in unixCandidates)
            {
                if (File.Exists(cand)) return cand;
            }
        }

        // 3. Search system PATH
        string exeName = OperatingSystem.IsWindows() ? "node.exe" : "node";
        string? onPath = FindOnPath(exeName);
        if (onPath != null) return onPath;

        // 4. Test default "node" command execution
        if (CanExecuteCommand(exeName, "--version"))
        {
            return exeName;
        }

        return null;
    }

    private static string? FindPythonExecutable()
    {
        // 1. Explicit environment override
        string? envPath = Environment.GetEnvironmentVariable("AIPROMPTCONTEXT_PYTHON_PATH");
        if (!string.IsNullOrEmpty(envPath) && File.Exists(envPath)) return envPath;

        if (OperatingSystem.IsWindows())
        {
            // Windows checks: python.exe, py.exe
            string? py = FindOnPath("python.exe") ?? FindOnPath("py.exe");
            if (py != null) return py;

            // Check standard LocalAppData Python installations
            string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string pyPrograms = Path.Combine(localApp, "Programs", "Python");
            if (Directory.Exists(pyPrograms))
            {
                var pyDirs = Directory.GetDirectories(pyPrograms, "Python*");
                foreach (var dir in pyDirs.OrderByDescending(d => d))
                {
                    string cand = Path.Combine(dir, "python.exe");
                    if (File.Exists(cand)) return cand;
                }
            }

            if (CanExecuteCommand("python", "--version")) return "python";
            if (CanExecuteCommand("py", "--version")) return "py";
        }
        else
        {
            var unixCandidates = new[]
            {
                "/usr/bin/python3",
                "/usr/local/bin/python3",
                "/opt/homebrew/bin/python3",
                "/usr/bin/python",
                "/usr/local/bin/python"
            };

            foreach (var cand in unixCandidates)
            {
                if (File.Exists(cand)) return cand;
            }

            string? p3 = FindOnPath("python3");
            if (p3 != null) return p3;

            string? p = FindOnPath("python");
            if (p != null) return p;

            if (CanExecuteCommand("python3", "--version")) return "python3";
            if (CanExecuteCommand("python", "--version")) return "python";
        }

        return null;
    }

    private static string? FindOnPath(string executableName)
    {
        string? pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv)) return null;

        var paths = pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        foreach (var p in paths)
        {
            try
            {
                string full = Path.Combine(p.Trim(), executableName);
                if (File.Exists(full)) return full;
            }
            catch
            {
                // Ignore invalid paths in PATH
            }
        }

        return null;
    }

    private static bool CanExecuteCommand(string fileName, string args)
    {
        try
        {
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            proc.Start();
            proc.WaitForExit(1000);
            return proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
