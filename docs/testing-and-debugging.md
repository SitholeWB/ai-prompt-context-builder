# Testing and Debugging Guide

This guide provides step-by-step instructions for testing, troubleshooting, and debugging **AI Context Builder (`aicontext`)** across the .NET Orchestrator, CLI, language workers, and VS Code extension.

---

## 1. Running the Automated Acceptance Test Suite

The test suite is self-contained and exercises all 13 core domains:
- Neutral dependency graph (multi-edges, cycle prevention, ASCII branch tree rendering)
- Importance scoring & depth attenuation
- Heuristic token estimation
- Syntax-aware comment removal (preserving Python docstrings, C# raw/verbatim strings)
- Security scanner (secret detection, path traversal protection)
- Markdown dynamic fence safety
- .NET Roslyn analyzer (classes, interfaces, records, constructor injection)
- Blazor / Razor analyzer (code-behind, scoped CSS companions, `@inject` services)
- Node.js worker (Angular templates, SCSS partials, React JSX components)
- Python worker (AST imports, classes, functions)
- Token budget selection algorithm (coherent chains, root file preservation)
- End-to-end CLI execution

### Run All Tests

From the repository root:

```bash
dotnet run --project tests/AiPromptContextBuilder.Tests.csproj
```

Expected output:
```text
=================================================
 AI Context Builder - Automated Acceptance Tests 
=================================================
[PASS] 1. Neutral Graph: Multi-Edges, Cycles, Tree Rendering
[PASS] 2. Scoring: Importance Weights & Depth Penalties
[PASS] 3. Token Estimation: Approximate Heuristic
[PASS] 4. Comment Removal: Docstring & String Preservation
[PASS] 5. Security: Secret Scanner & Traversal Prevention
[PASS] 6. Markdown: Dynamic Fence Safety & Section Output
[PASS] 7. .NET / Roslyn Analyzer: C#, Interfaces, Constructors
[PASS] 8. Blazor / Razor Analyzer: Companions & Injections
[PASS] 9. Node Worker: Angular Template & SCSS Traversal
[PASS] 10. Node Worker: React JSX Component Dependencies
[PASS] 11. Python Worker: AST Imports & Docstrings
[PASS] 12. Budget Selection: Coherent Chains & Auto Comment Mode
[PASS] 13. End-to-End CLI: Full Context Generation
=================================================
Tests Completed: 13 Passed, 0 Failed
=================================================
```

---

## 2. Debugging the .NET Orchestrator & CLI

### 2.1 Using VS Code Debugger

The repository includes a ready-to-use `.vscode/launch.json`:
1. Open the repository in VS Code / Antigravity IDE.
2. Go to the **Run & Debug** panel (`Ctrl+Shift+D`).
3. Select **"Debug CLI (C# CustomerService)"** or **"Debug Acceptance Tests"**.
4. Set breakpoints in `src/orchestrator/cli/Program.cs`, `ContextOrchestrator.cs`, or `DotNetAnalyzer.cs`.
5. Press **F5** to start debugging.

### 2.2 Using Visual Studio 2022

1. Open `AiPromptContextBuilder.slnx` in Visual Studio 2022.
2. Right-click `AiPromptContextBuilder.Cli` in Solution Explorer -> **Set as Startup Project**.
3. Right-click `AiPromptContextBuilder.Cli` -> **Properties** -> **Debug** -> **General** -> **Open debug launch profiles UI**.
4. Set Command Line Arguments:
   ```text
   generate --file tests/fixtures/dotnet/CustomerService.cs --verbose
   ```
5. Press **F5** to attach the debugger with full symbol resolution and Edit & Continue.

### 2.3 Verbose Diagnostics via CLI

When diagnosing graph traversal, token budgeting, or file exclusions from the terminal, pass `--verbose` and `--format json`:

```bash
./bin/aicontext generate \
  --file tests/fixtures/dotnet/CustomerService.cs \
  --verbose \
  --format json
```

Inspect the returned JSON payload:
- `warnings`: Discloses any unresolvable dynamic imports or reflection.
- `diagnostics`: Structured diagnostic items with file, line, and severity.
- `filesExcluded`: Identifies which files were dropped and why.

---

## 3. Debugging Language Workers

The workers communicate over standard input/output using a versioned JSON protocol. You can inspect and test each worker independently without running the orchestrator.

### 3.1 Node.js Worker (`src/workers/node/host/worker.js`)

#### A. Interactive Pipe Testing
Send a raw JSON request to verify TypeScript/React/Angular analysis:

```bash
echo '{"protocolVersion":"1.0","requestId":"debug-1","operation":"analyse","rootPath":"tests/fixtures/node/customer.component.ts","workspacePath":"tests/fixtures/node"}' | ./bin/node src/workers/node/host/worker.js
```

Verify that the response returns `"success": true` with discovered template and style companion nodes.

#### B. Node Inspector Breakpoints
Run the worker with the Node debugger:

```bash
./bin/node --inspect-brk src/workers/node/host/worker.js
```

Open Chrome or Edge at `chrome://inspect` and connect to debug AST nodes and TypeScript Compiler API queries.

---

### 3.2 Python Worker (`src/workers/python/aicontext/worker.py`)

#### A. Interactive Pipe Testing
Test Python AST traversal and import resolution:

```bash
echo '{"protocolVersion":"1.0","requestId":"debug-2","operation":"analyse","rootPath":"tests/fixtures/python/service.py","workspacePath":"tests/fixtures/python"}' | python3 src/workers/python/aicontext/worker.py
```

#### B. Debugging with `pdb`
Add `import pdb; pdb.set_trace()` inside `worker.py` in the `analyze()` function to step through AST nodes interactively.

---

### 3.3 .NET Roslyn Worker (`AiPromptContextBuilder.Workers.DotNet`)

The .NET worker runs in-process with the orchestrator. You can set breakpoints inside:
- `DotNetAnalyzer.cs`:
  - `AnalyzeCSharpFile()`: Inspects Roslyn syntax trees, base types, constructor parameters, and partial declarations.
  - `AnalyzeRazorOrBlazor()`: Inspects companion `.razor.cs`, scoped CSS `.razor.css`, and `@inject` service bindings.
  - `FindFileForType()`: Resolves local symbol types to physical files in the workspace.

---

## 4. Testing Edge Cases & Error Codes

AI Context Builder emits stable error codes on failure. You can test each error condition:

| Condition | Command | Expected Result |
| :--- | :--- | :--- |
| **Missing File** | `./bin/aicontext generate --file non_existent.cs --format json` | `ErrorCode: "FileNotFound"` |
| **Path Traversal Escape** | `./bin/aicontext generate --file ../../outside.cs --format json` | `ErrorCode: "OutsideWorkspace"` |
| **Multiple Candidates** | `./bin/aicontext generate --file tests/fixtures/python/service.py --format json` | `ErrorCode: "MultipleRootSymbols"` with candidate list |
| **Missing Options** | `./bin/aicontext generate` | `ErrorCode: "InvalidConfiguration"` |

---

## 5. Adding New Test Fixtures & Golden-File Testing

1. Add your source file in `tests/fixtures/<language>/`.
2. Generate the context using `--omit-timestamp`:
   ```bash
   ./bin/aicontext generate \
     --file tests/fixtures/<language>/YourFile.<ext> \
     --output tests/golden-files/<language>-expected.md \
     --omit-timestamp
   ```
3. Commit the golden file. Subsequent test runs can compare the generated output byte-for-byte against the golden file to detect any regressions.
