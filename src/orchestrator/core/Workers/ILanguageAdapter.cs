using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AiContextBuilder.Core.Protocol;

namespace AiContextBuilder.Core.Workers;

public interface ILanguageAdapter
{
    string Id { get; }
    CapabilityLevel Capability { get; }
    IReadOnlyList<string> SupportedExtensions { get; }
    bool CanHandle(string language, string extension);
    Task<DependencyGraphFragment> AnalyzeAsync(
        string filePath,
        string workspaceRoot,
        UserConfiguration config,
        CancellationToken cancellationToken = default);
}
