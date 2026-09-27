# AI Context Builder - Architecture Documentation

## 1. System Overview

AI Context Builder (`aicontext`) is a high-performance, local, dependency-aware context generation platform designed for AI-assisted software refactoring, debugging, documentation, and code analysis.

The system analyzes a selected root source file or symbol, discovers its direct and transitive dependencies across multiple languages and frameworks, calculates deterministic importance scores, applies configurable token budgets and comment preservation policies, and generates exactly one authoritative Markdown document containing complete, untruncated source files with their original repository-relative paths.

```
VS Code Extension / Visual Studio VSIX / CI / CLI
                       |
                       v
         Context Orchestrator CLI (`aicontext`)
                       |
       +---------------+---------------+
       |                               |
  Shared Core:                    Workers:
  - Neutral Dependency Graph       - In-Process .NET Worker (C#, VB.NET, Razor, Blazor, Roslyn)
  - Upward Workspace Discovery     - Node.js Worker (TypeScript Compiler API, React, Angular, Vue, HTML, SCSS)
  - Deterministic Scoring Engine   - Python Worker (AST, tokenize, import analysis)
  - Token Budget Selector          - Java & Go Analyzers (Project structure & syntax)
  - Secret Preflight Scanner
  - Safe Markdown Generator
```

## 2. Core Architectural Principles

1. **Complete Source Files**: Every included file is rendered in full. No summarization, truncation, pseudo-code, or omissions.
2. **Preserve Original Relative Paths**: Workspace-relative paths are preserved without exposing absolute filesystem paths.
3. **One Output Artifact**: Exactly one self-contained Markdown file is generated.
4. **No Hidden Limits**: When no token budget is provided, all eligible dependencies are included without artificial file count or traversal depth limits.
5. **Deterministic Execution**: Decisions are algorithmic and mathematical—no remote or local AI model makes inclusion or exclusion decisions.

## 3. Worker Protocol

Language analyzers communicate via a stable, versioned JSON protocol over standard I/O (`stdin`/`stdout`):
- `WorkerProtocolRequest`: `{ protocolVersion, requestId, operation, rootPath, workspacePath, language, options }`
- `WorkerProtocolResponse`: `{ protocolVersion, requestId, success, adapter, graph: { nodes, edges, diagnostics, warnings } }`
No source code is ever transmitted over network interfaces.
