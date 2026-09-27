using System;
using System.IO;

namespace AiContextBuilder.Core.Workers;

public static class WorkerLocator
{
    /// <summary>
    /// Searches upward from the execution and working directories to find the relative worker script.
    /// </summary>
    public static string LocateWorkerScript(string relativePath)
    {
        // 1. Search upwards from AppContext.BaseDirectory
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(candidate))
                return Path.GetFullPath(candidate);
            dir = dir.Parent;
        }

        // 2. Search upwards from Directory.GetCurrentDirectory()
        dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(candidate))
                return Path.GetFullPath(candidate);
            dir = dir.Parent;
        }

        // 3. Fallback
        return Path.GetFullPath(relativePath);
    }

    /// <summary>
    /// Locates the appropriate Node.js executable.
    /// </summary>
    public static string LocateNodeExecutable()
    {
        // Default to system PATH node
        return "node";
    }

    /// <summary>
    /// Locates the appropriate Python executable.
    /// </summary>
    public static string LocatePythonExecutable()
    {
        return OperatingSystem.IsWindows() ? "python" : "python3";
    }
}
