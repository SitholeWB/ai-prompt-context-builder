# Supported Languages and Frameworks

AI Context Builder V1 supports the following languages, frameworks, and capability levels:

| Language / File Type | Extensions | Engine / Worker | Capability Level |
| :--- | :--- | :--- | :--- |
| **C#** | `.cs` | .NET Roslyn (`Microsoft.CodeAnalysis.CSharp`) | Semantic |
| **Visual Basic .NET** | `.vb` | .NET Roslyn (`Microsoft.CodeAnalysis.VisualBasic`) | Semantic |
| **ASP.NET Core** | `.cs` | .NET Controller & Minimal API Enhancer | Semantic |
| **Blazor & Razor** | `.razor`, `.cshtml` | Blazor Component & Scoped CSS Enhancer | SyntaxAware + CompanionFile |
| **TypeScript** | `.ts`, `.mts`, `.cts` | TypeScript Compiler API 5.9.3 | Semantic |
| **JavaScript** | `.js`, `.mjs`, `.cjs` | TypeScript Compiler API | Semantic |
| **JSX & TSX** | `.jsx`, `.tsx` | TypeScript Compiler API + React Enhancer | Semantic + SyntaxAware |
| **React** | `.jsx`, `.tsx` | React Component & Hook Enhancer | Semantic + SyntaxAware |
| **Angular** | `.ts`, `.html`, `.scss` | Angular Decorator & Template Enhancer | Semantic + CompanionFile |
| **Vue SFC** | `.vue` | Vue SFC Block Analyzer (One file in output) | SyntaxAware |
| **HTML** | `.html`, `.htm` | Syntax HTML Parser (Scripts, Styles, Import Maps) | SyntaxAware |
| **CSS, SCSS, Sass** | `.css`, `.scss`, `.sass` | SCSS & Sass Parser (`@import`, `@use`, partials) | SyntaxAware |
| **Python** | `.py` | Python 3 `ast` & `tokenize` Worker | Semantic |
| **Java** | `.java` | Java Syntax & Maven/Gradle Project Analyzer | SyntaxAware |
| **Go** | `.go` | Go Packages & `go.mod` Syntax Analyzer | SyntaxAware |
| **JSON / YAML Config** | `.json`, `.yaml`, `.yml` | Configuration File Analyzer | ImportGraph |
