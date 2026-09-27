using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using AiContextBuilder.Core.Protocol;

namespace AiContextBuilder.Core.Workers;

public static class WorkerClient
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static async Task<WorkerProtocolResponse> ExecuteExternalWorkerAsync(
        string executable,
        string arguments,
        WorkerProtocolRequest request,
        CancellationToken cancellationToken = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = executable,
            Arguments = arguments,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // Configure Node.js environment dynamically if IDE extensions path is available
        string? homeDir = Environment.GetEnvironmentVariable("HOME") ?? Environment.GetEnvironmentVariable("USERPROFILE");
        if (!string.IsNullOrEmpty(homeDir))
        {
            string ideNodeModules = Path.Combine(homeDir, ".local", "share", "antigravity-ide", "resources", "app", "extensions", "node_modules");
            if (Directory.Exists(ideNodeModules))
            {
                psi.Environment["ELECTRON_RUN_AS_NODE"] = "1";
                string existingNodePath = Environment.GetEnvironmentVariable("NODE_PATH") ?? "";
                psi.Environment["NODE_PATH"] = string.IsNullOrEmpty(existingNodePath)
                    ? ideNodeModules
                    : $"{ideNodeModules}{Path.PathSeparator}{existingNodePath}";
            }
        }

        using var process = new Process { StartInfo = psi };
        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            return new WorkerProtocolResponse
            {
                ProtocolVersion = "1.0",
                RequestId = request.RequestId,
                Success = false,
                ErrorCode = "WorkerStartFailed",
                ErrorMessage = $"Failed to start worker process '{executable}': {ex.Message}"
            };
        }

        string jsonRequest = JsonSerializer.Serialize(request, JsonOpts);
        await process.StandardInput.WriteLineAsync(jsonRequest.AsMemory(), cancellationToken);
        await process.StandardInput.FlushAsync(cancellationToken);

        string? jsonResponse = await process.StandardOutput.ReadLineAsync(cancellationToken);
        if (string.IsNullOrEmpty(jsonResponse))
        {
            string stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
            return new WorkerProtocolResponse
            {
                ProtocolVersion = "1.0",
                RequestId = request.RequestId,
                Success = false,
                ErrorCode = "WorkerProtocolError",
                ErrorMessage = $"Worker returned empty response. Stderr: {stderr}"
            };
        }

        try
        {
            var res = JsonSerializer.Deserialize<WorkerProtocolResponse>(jsonResponse, JsonOpts);
            return res ?? new WorkerProtocolResponse
            {
                Success = false,
                ErrorCode = "WorkerProtocolError",
                ErrorMessage = "Deserialized null response from worker."
            };
        }
        catch (Exception ex)
        {
            return new WorkerProtocolResponse
            {
                Success = false,
                ErrorCode = "WorkerProtocolError",
                ErrorMessage = $"JSON parsing error from worker output: {ex.Message}"
            };
        }
    }
}
