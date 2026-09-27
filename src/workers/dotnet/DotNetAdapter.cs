using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AiContextBuilder.Core.Protocol;
using AiContextBuilder.Core.Workers;

namespace AiContextBuilder.Workers.DotNet;

public class DotNetAdapter : ILanguageAdapter
{
    private readonly DotNetAnalyzer _analyzer = new();

    public string Id => "DotNetAnalyzer";
    public CapabilityLevel Capability => CapabilityLevel.Semantic;

    public IReadOnlyList<string> SupportedExtensions => new[]
    {
        ".cs", ".vb", ".razor", ".cshtml"
    };

    public bool CanHandle(string language, string extension)
    {
        return SupportedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase) ||
               language is "CSharp" or "VisualBasic" or "Razor";
    }

    public Task<DependencyGraphFragment> AnalyzeAsync(
        string filePath,
        string workspaceRoot,
        UserConfiguration config,
        CancellationToken cancellationToken = default)
    {
        var fragment = _analyzer.Analyze(filePath, workspaceRoot, config);
        return Task.FromResult(fragment);
    }
}
