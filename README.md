# AI Context Builder (`aicontext`)

**AI Context Builder** is a production-quality, local-only developer tool that generates portable, dependency-aware, AI-ready source context from an existing repository.

A developer selects a source file or symbol. The application analyzes the source, discovers direct and transitive dependencies across multiple languages and frameworks, identifies companion files (templates, stylesheets, code-behinds), applies configurable token and comment policies, and generates **exactly one Markdown document** containing complete source files with their original repository-relative paths.

---

## 1. Product Principles

1. **Complete Source Files**: Every included file is included in full. Never summarized, truncated, pseudo-coded, or replaced with ellipses.
2. **Preserve Original Paths**: Every file displays its repository-relative path (e.g. `src/Application/Customers/CustomerService.cs`). Absolute paths are never exposed.
3. **One Output Artifact**: The primary output is exactly one Markdown document (e.g., `customer-service-context.md`).
4. **No Hidden Limits**: When no token budget is specified, all eligible transitive dependencies are included in full without arbitrary file-count or depth caps.
5. **Transparent Disclosures**: Diagnostics, dynamic relationships, capability levels, and exclusion reasons are clearly documented in the generation metadata.
6. **Local-Only & Private**: 100% local execution. No code upload, no remote AI API requirements, and zero telemetry.

---

## 2. Architecture

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

- **Core & CLI**: Built in **.NET 10 / C#** for instant native execution, strongly typed graph algorithms, and in-process Roslyn integration.
- **Workers**: Language-native workers communicate over standard input/output with a versioned JSON protocol (`protocolVersion: "1.0"`).

---

## 3. Supported Languages & Ecosystems

| Language / Framework | Engine / Adapter | Capability Level |
| :--- | :--- | :--- |
| **C#** (`.cs`) | .NET Roslyn Compiler API | Semantic |
| **Visual Basic .NET** (`.vb`) | .NET Roslyn Compiler API | Semantic |
| **ASP.NET Core** | Controller & Minimal API Enhancer | Semantic |
| **Blazor & Razor** (`.razor`, `.cshtml`) | Razor & Scoped CSS Companion Enhancer | SyntaxAware + CompanionFile |
| **TypeScript** (`.ts`, `.mts`, `.cts`) | TypeScript Compiler API 5.9.3 | Semantic |
| **JavaScript** (`.js`, `.mjs`, `.cjs`) | TypeScript Compiler API 5.9.3 | Semantic |
| **React** (`.jsx`, `.tsx`) | React Component & Hook Enhancer | Semantic + SyntaxAware |
| **Angular** (`.ts`, `.html`, `.scss`) | Angular Decorator & Template Enhancer | Semantic + CompanionFile |
| **Vue SFC** (`.vue`) | Single-File Component Block Analyzer | SyntaxAware |
| **HTML** (`.html`, `.htm`) | HTML Syntax Analyzer (scripts, styles) | SyntaxAware |
| **CSS, SCSS, Sass** | SCSS & Sass Parser (`@import`, `@use`, partials) | SyntaxAware |
| **Python** (`.py`) | Python AST & Tokenize Worker | Semantic |
| **Java** (`.java`) | Java Syntax & Maven/Gradle Analyzer | SyntaxAware |
| **Go** (`.go`) | Go Packages & `go.mod` Syntax Analyzer | SyntaxAware |

---

## 4. Quick Start & CLI Usage

### Build the Solution

```bash
dotnet build
```

### CLI Command Examples

```bash
# Analyze a C# service with unlimited budget
./bin/aicontext generate --file src/Application/Customers/CustomerService.cs

# Analyze an Angular component with template and style companions
./bin/aicontext generate --file src/app/customers/customer.component.ts --framework angular

# Analyze a React page with a 100,000 token budget and auto comment removal
./bin/aicontext generate --file src/components/CustomerPage.tsx --max-tokens 100000 --comments auto

# Analyze Python service and output machine-readable JSON metadata
./bin/aicontext generate --file app/services/customer_service.py --format json

# Verify installed language workers
./bin/aicontext check-workers
```

### Options Reference

| Option | Description |
| :--- | :--- |
| `--file <path>` | **(Required)** Target source file to analyze |
| `--workspace <path>` | Workspace boundary directory |
| `--project <path>` | Explicit project file (`.csproj`, `tsconfig.json`, `pom.xml`, etc.) |
| `--symbol <name>` | Explicit declaration symbol to select as root |
| `--output <path>` | Destination Markdown file path |
| `--max-tokens <int>` | Token budget (omitted means unlimited) |
| `--max-depth <int>` | Maximum dependency traversal depth (omitted means unlimited) |
| `--comments <mode>` | `preserve`, `remove`, or `auto` (default: `preserve`) |
| `--task <text>` | Custom refactoring or analysis prompt header |
| `--include-tests <bool>` | Include test files (default: `false`) |
| `--include-generated <bool>` | Include compiler-generated files (default: `false`) |
| `--include-companions <bool>` | Include framework companion templates & styles (default: `true`) |
| `--format text\|json` | Output format (default: `text`) |
| `--omit-timestamp` | Omit timestamp for deterministic golden testing |

---

## 5. VS Code Extension

Located in `src/extensions/vscode/`:
- **Commands**: `Generate AI Context`, `Generate from Symbol at Cursor`, `Generate with Options...`, `Open Last Generated Context`, `Show Dependency Preview`, `Check Language Workers`.
- **Context Menus**: Right-click any file in Explorer or Editor to instantly generate AI context.
- **Progress Reporting**: Real-time cancellable progress across all analysis phases.

---

## 6. Automated Acceptance Tests

Run the full automated test suite covering all multi-language analyzers, scoring, token budget algorithms, and security scanners:

```bash
dotnet run --project tests/AiContextBuilder.Tests.csproj
```

Output:
```
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

## 7. License

MIT License. See [LICENSE](LICENSE) for details.
