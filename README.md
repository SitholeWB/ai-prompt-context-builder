<div align="center">

# 🧠 AI Context Builder (`aicontext`)

**Generate portable, dependency-aware, AI-ready source context from an existing repository.**

[![CI](https://github.com/aicontext/ai-context-builder/actions/workflows/ci.yml/badge.svg)](https://github.com/aicontext/ai-context-builder/actions)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![C#](https://img.shields.io/badge/C%23-12.0-239120?logo=csharp)](https://learn.microsoft.com/en-us/dotnet/csharp/)
[![Roslyn](https://img.shields.io/badge/Roslyn-Semantic_Compiler_API-blue)](https://github.com/dotnet/roslyn)
[![Node.js](https://img.shields.io/badge/Node.js-22.x-339933?logo=nodedotjs)](https://nodejs.org/)
[![TypeScript](https://img.shields.io/badge/TypeScript-5.9_Compiler_API-3178C6?logo=typescript)](https://www.typescriptlang.org/)
[![Python](https://img.shields.io/badge/Python-3.x_AST-3776AB?logo=python)](https://www.python.org/)
[![Privacy](https://img.shields.io/badge/Privacy-100%25_Local_Only-success)](#-security--privacy-guarantee)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

*A developer selects a file or symbol. AI Context Builder analyzes the source, discovers direct and transitive dependencies across multiple languages, links framework companion files, applies deterministic token and comment policies, and generates exactly one authoritative Markdown document with complete, untruncated source files.*

[Key Principles](#-product-principles) • [VS Code Extension](#-vs-code-extension-guide) • [CLI Quickstart](#-cli-quickstart) • [Architecture](#-architecture) • [Testing & Debugging](#-testing--debugging)

</div>

---

## 🚀 Why AI Context Builder?

When feeding code to LLMs (Claude, GPT, Gemini, DeepSeek) for refactoring, debugging, architecture reviews, or migrations, standard approaches fail:
- **Copy-pasting individual files** leaves out essential interfaces, models, and constructor dependencies.
- **Dumping entire repositories** blows through context limits and confuses the model with irrelevant noise.
- **Naive AI summarization tools** truncate methods, replace real code with ellipses (`// ...rest unchanged`), and strip critical implementation details.

**AI Context Builder solves this:**
- 🛡️ **Complete Source Files**: Every included file is included in full. Never summarized, truncated, pseudo-coded, or replaced with ellipses.
- 📁 **Preserve Original Relative Paths**: Relative workspace paths (e.g. `src/Application/Customers/CustomerService.cs`) are preserved verbatim. Absolute paths are never exposed.
- 📄 **One Output Artifact**: The entire dependency-aware context lives in exactly one self-contained, beautifully formatted Markdown document (`*-context.md`).
- ⚖️ **No Hidden Limits**: When no token budget is specified, all eligible transitive dependencies are traversed without artificial depth or file-count caps.
- 🔒 **100% Local & Private**: Runs strictly locally. No source code upload, no external API keys required, and zero telemetry.

---

## 🧩 Architecture

```text
Visual Studio Code Extension / Visual Studio VSIX / CI / CLI
                             |
                             v
               Context Orchestrator CLI (`aicontext`)
                             |
             +---------------+---------------+
             |                               |
        Shared Core (.NET 10):          Workers (Language-Neutral Protocol):
        - Neutral Dependency Graph       - In-Process .NET Worker (C#, VB.NET, Razor, Blazor, Roslyn)
        - Upward Workspace Discovery     - Node.js Worker (TypeScript Compiler API 5.9, React, Angular, Vue, HTML, SCSS)
        - Deterministic Scoring Engine   - Python Worker (AST, tokenize, import analysis)
        - Token Budget Selector          - Java & Go Analyzers (Project metadata & syntax)
        - Secret Preflight Scanner
        - Safe Markdown Generator
```

The orchestrator is implemented in **.NET 10 / C#** for instant native CLI execution and in-process **Roslyn** semantic analysis, while dedicated workers communicate via standard I/O using a versioned JSON protocol (`protocolVersion: "1.0"`).

---

## 🌐 Supported Languages & Capability Matrix

Every analyzer explicitly discloses its capability level in the generated Markdown metadata:

| Language / File Type | Extensions | Engine / Worker | Capability Level |
| :--- | :--- | :--- | :--- |
| **C#** | `.cs` | .NET Roslyn (`Microsoft.CodeAnalysis.CSharp`) | **Semantic** |
| **Visual Basic .NET** | `.vb` | .NET Roslyn (`Microsoft.CodeAnalysis.VisualBasic`) | **Semantic** |
| **ASP.NET Core** | `.cs` | Controller, Minimal API & DI Enhancer | **Semantic** |
| **Blazor & Razor** | `.razor`, `.cshtml` | Scoped CSS & Companion Code-Behind Enhancer | **SyntaxAware + CompanionFile** |
| **TypeScript** | `.ts`, `.mts`, `.cts` | TypeScript Compiler API 5.9.3 | **Semantic** |
| **JavaScript** | `.js`, `.mjs`, `.cjs` | TypeScript Compiler API 5.9.3 | **Semantic** |
| **React** | `.jsx`, `.tsx` | React Component, Hook & Context Enhancer | **Semantic + SyntaxAware** |
| **Angular** | `.ts`, `.html`, `.scss` | Angular `@Component`, `templateUrl`, `styleUrls` | **Semantic + CompanionFile** |
| **Vue SFC** | `.vue` | Single-File Component Parser (single file in output) | **SyntaxAware** |
| **HTML** | `.html`, `.htm` | HTML Syntax Analyzer (scripts, module scripts, styles) | **SyntaxAware** |
| **CSS, SCSS, Sass** | `.css`, `.scss`, `.sass` | SCSS & Sass Syntax Parser (`@import`, `@use`, partials) | **SyntaxAware** |
| **Python** | `.py` | Python 3 `ast` & `tokenize` Worker | **Semantic** |
| **Java** | `.java` | Java Syntax & Maven/Gradle Project Analyzer | **SyntaxAware** |
| **Go** | `.go` | Go Packages & `go.mod` Syntax Analyzer | **SyntaxAware** |
| **JSON / YAML Config**| `.json`, `.yaml`, `.yml`| Explicitly Referenced Configuration Analyzer | **ImportGraph** |

---

## 💻 CLI Quickstart

### Build the Solution

```bash
dotnet build
```

The compiled CLI wrapper is located at `./bin/aicontext`.

### Usage Examples

```bash
# 1. Analyze C# Service with unlimited token budget
./bin/aicontext generate --file src/Application/Customers/CustomerService.cs

# 2. Analyze Angular Component with HTML template and SCSS partials
./bin/aicontext generate --file src/app/customers/customer.component.ts --framework angular

# 3. Analyze React Component with a 64k token budget and auto comment stripping
./bin/aicontext generate --file src/components/CustomerPage.tsx --max-tokens 64000 --comments auto

# 4. Analyze Python module and output machine-readable JSON metadata
./bin/aicontext generate --file app/services/customer_service.py --format json

# 5. Check active language workers
./bin/aicontext check-workers
```

### Options Reference

```text
Usage:
  aicontext generate --file <path> [options]
  aicontext check-workers

Required:
  --file <path>                  Target source file to analyze

Options:
  --workspace <path>             Workspace root directory boundary
  --project <path>               Explicit project file (.csproj, tsconfig.json, etc.)
  --symbol <qualified-name>      Specific class, function, or symbol to select as root
  --output <path>                Destination Markdown file path
  --max-tokens <int>             Token budget (omitted means unlimited)
  --max-depth <int>              Maximum dependency traversal depth (omitted means unlimited)
  --comments preserve|remove|auto Comment preservation policy (default: preserve)
  --language <lang>              Force specific language analyzer
  --framework <fw>               Force specific framework adapter
  --analysis-level <level>       Best, Semantic, SyntaxAware, ImportGraph
  --task <text>                  Custom prompt task heading
  --include-tests <bool>         Include test files (default: false)
  --include-generated <bool>     Include generated files (default: false)
  --include-attributes <bool>    Include attributes/decorators (default: false)
  --include-companions <bool>    Include companion templates/styles (default: true)
  --include-config <bool>        Include configuration files (default: true)
  --save <bool>                  Write result to file (default: true)
  --copy <bool>                  Copy result to clipboard (default: false)
  --format text|json             Output format (default: text)
  --omit-timestamp               Omit generation timestamp for deterministic golden testing
  --verbose                      Enable verbose diagnostic logging
  --help                         Display help message
  --version                      Display version information
```

---

## 🔌 VS Code Extension Guide

The official VS Code extension is located in `src/extensions/vscode/`.

### Commands

| Command | Title | Description |
| :--- | :--- | :--- |
| `aiContextBuilder.generateFromFile` | **AI Context: Generate from Current File** | Analyzes the active editor document |
| `aiContextBuilder.generateFromSymbol`| **AI Context: Generate from Symbol at Cursor**| Analyzes the specific class/function under cursor |
| `aiContextBuilder.generateWithOptions`| **AI Context: Generate with Options...** | Interactive QuickPick wizard for budget, depth, and comments |
| `aiContextBuilder.generateAndCopy` | **AI Context: Generate and Copy** | Copies generated Markdown directly to clipboard |
| `aiContextBuilder.generateAndSave` | **AI Context: Generate and Save** | Generates and saves to file |
| `aiContextBuilder.openLastGenerated`| **AI Context: Open Last Generated Context** | Opens previous context document in editor |
| `aiContextBuilder.showDependencyPreview` | **AI Context: Show Dependency Preview** | Displays ASCII dependency tree notification |
| `aiContextBuilder.checkWorkers` | **AI Context: Check Language Workers** | Verifies installed compilers and runtime workers |

### Context Menus
- **Editor Context Menu**: Right-click anywhere in code -> **Generate AI Context**.
- **Explorer Context Menu**: Right-click any file in the file tree -> **Generate AI Context**.

### Extension Settings (`aiContextBuilder.*`)

```json
{
  "aiContextBuilder.executablePath": "aicontext",
  "aiContextBuilder.defaultTokenBudget": null,
  "aiContextBuilder.commentMode": "Preserve",
  "aiContextBuilder.includeCompanionFiles": true,
  "aiContextBuilder.includeTests": false,
  "aiContextBuilder.includeGeneratedFiles": false,
  "aiContextBuilder.openAfterGeneration": true
}
```

---

## 🔒 Security & Privacy Guarantee

- **Zero Cloud Leakage**: No remote AI API calls, telemetry, or external network requests.
- **Preflight Secret Scanner**: Inspects candidate files before inclusion for private keys, AWS/Slack/GitHub keys, and database connection strings with passwords. Secret values are **never** printed to logs or Markdown files.
- **Path Traversal Protection**: Canonicalizes all file paths and enforces strict workspace boundaries. Symlinks escaping the workspace and `.git` internals are automatically rejected.

---

## 🧪 Testing & Debugging

The repository features comprehensive automated acceptance tests and step-by-step developer debugging tools:
- **Run Acceptance Tests**: `dotnet run --project tests/AiContextBuilder.Tests.csproj`
- **VS Code F5 Debugging**: Pre-configured `.vscode/launch.json` for stepping through CLI execution and tests.
- **Detailed Guide**: See [`docs/testing-and-debugging.md`](docs/testing-and-debugging.md) for worker pipe debugging, AST inspection, and golden-file regression workflows.

---

## 📚 Documentation Index

- [Architecture Design & Data Flow](docs/architecture.md)
- [Language Adapter Contract](docs/adapter-contract.md)
- [Dependency Semantics & Importance Scoring](docs/dependency-semantics.md)
- [Security & Workspace Boundaries](docs/security.md)
- [Supported Languages Reference](docs/supported-languages.md)
- [Testing and Debugging Guide](docs/testing-and-debugging.md)
- [Visual Studio Extension Plan (VSIX)](docs/visual-studio-extension-plan.md)
- [Known Limitations & Transparent Disclosures](docs/known-limitations.md)

---

## 📄 License

Distributed under the [MIT License](LICENSE).
