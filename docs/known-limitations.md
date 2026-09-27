# Known Limitations and Transparent Disclosures

In accordance with Section 1.5 of the product specification, AI Context Builder transparently discloses the boundaries of static analysis across all supported languages and ecosystems:

## 1. Dynamic Code Execution
- **Python**: Dynamic module imports (`importlib.import_module`, `__import__`), dynamic member dispatch (`getattr`, `setattr`), and runtime monkey-patching cannot be fully resolved statically. They are flagged as warnings in the output document.
- **JavaScript / TypeScript**: Non-literal dynamic imports (e.g. `import(variablePath)`) and `eval()` cannot be resolved statically.
- **C# / .NET**: Reflection invocations (`Type.GetType(string)`, `Assembly.Load`), dynamic compilation (`System.Reflection.Emit`), and runtime dependency injection containers without compile-time registrations cannot be statically bound.

## 2. Cross-Language Route Inference
- Route matching between frontend HTTP client calls (e.g. `fetch('/api/customers')` or `HttpClient.GetAsync`) and backend API controllers is not inferred based solely on string matching, preventing false positive relationships.

## 3. Remote Resources and Binaries
- External URL references (`http://`, `https://`) in HTML, CSS, or scripts are not fetched over the network.
- Binary assets (images, videos, compiled assemblies, fonts) are listed as metadata-only or excluded to protect Markdown document integrity.

## 4. External NuGet / npm / Maven Packages
- Source code from `node_modules`, global NuGet caches, or Maven `.m2` repositories is excluded by default unless explicitly configured. External dependencies are represented as metadata nodes in the dependency tree.
