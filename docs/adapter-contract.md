# Language Adapter & Framework Enhancer Contract

## 1. Language Adapter Interface

Every language adapter implements the decoupled contract:

```csharp
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
```

## 2. Analysis Capability Levels

Every adapter declares its capability:
- `Semantic`: Uses compiler symbol resolution (Roslyn, TypeScript Compiler API). Resolves declarations, types, and calls with high confidence.
- `SyntaxAware`: Real syntax parser and AST (e.g. Python `ast`, Angular template parser), but without full type solving.
- `ImportGraph`: Follows imports, exports, and module paths.
- `CompanionFile`: Associates files through framework conventions (e.g., `.razor.cs`, `.razor.css`, Angular `templateUrl`).
- `ReferenceOnly`: Detects dependencies that cannot be safely included as text source.

## 3. Worker Discovery

Adapters are registered with `ContextOrchestrator.RegisterAdapter(ILanguageAdapter)`.
Workers can be:
1. **In-process**: Direct assembly reference (e.g., `DotNetAdapter` using Roslyn).
2. **External Process**: Child process communicating via standard input/output JSON protocol (e.g., `NodeWorkerAdapter` invoking `worker.js`, `PythonWorkerAdapter` invoking `worker.py`).
