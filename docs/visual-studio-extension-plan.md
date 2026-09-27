# Visual Studio Extension Plan (VSIX)

## 1. Executive Summary

This document specifies the integration architecture for extending **AI Context Builder** into a native **Visual Studio 2022+ extension** (`.vsix`). 

Because the orchestrator and .NET worker are built with .NET and Roslyn, Visual Studio integration can directly reuse `AiContextBuilder.Core` as a referenced library without IPC or child-process overhead.

## 2. Core Architectural Principles for Visual Studio

1. **Keep Shared Libraries Free of Visual Studio SDK**:
   `AiContextBuilder.Core` must remain pure .NET Standard / .NET 8 / .NET 10 without any dependencies on `Microsoft.VisualStudio.Shell.*`.
2. **Reuse In-Process Roslyn Workspace**:
   Instead of parsing disk files from scratch, the Visual Studio extension passes the active `VisualStudioWorkspace` and `EnvDTE.Document` directly to `DotNetAnalyzer`.
3. **Handle Unsaved Memory Buffers**:
   Visual Studio allows developers to analyze code with pending unsaved edits. The extension intercepts `ITextBuffer` / `Document.GetTextAsync()` and provides the live text in memory.

## 3. Extension Components

### 3.1 `AsyncPackage` Entry Point
- Inherits from `Microsoft.VisualStudio.Shell.AsyncPackage`.
- Registers menu commands asynchronously via `OleMenuCommandService` on background initialization.
- Queries `IVsOutputWindow` to allocate an dedicated "AI Context Builder" pane.

### 3.2 UI Commands
- **Editor Context Menu**: `Generate AI Context` (runs on current document or symbol at caret).
- **Solution Explorer Context Menu**: `Generate AI Context` (runs on right-clicked file or project node).
- **Tools Menu**: `AI Context: Settings...` (links to Tools -> Options).

### 3.3 Symbol-at-Caret Integration
- Acquires `IVsTextView` from `IVsEditorAdaptersFactoryService`.
- Retrieves cursor line and character offset.
- Calls `Microsoft.CodeAnalysis.Text.SourceText` and `Microsoft.CodeAnalysis.FindSymbols.GetSymbolAtPositionAsync()` from Roslyn.
- Supplies the resolved qualified symbol to `UserConfiguration.RootSymbol`.

### 3.4 Unsaved Buffers & Live Compilation State
- Retrieves `Microsoft.CodeAnalysis.Document` from `VisualStudioWorkspace.CurrentSolution`.
- Passes the active `Compilation` and live syntax trees directly into `DotNetAnalyzer`, eliminating disk I/O and incorporating unsaved refactorings.

### 3.5 Options Page (`DialogPage`)
- Registered under `Tools -> Options -> AI Context Builder`.
- Exposes:
  - Default Token Budget
  - Default Comment Mode (`Preserve`, `Remove`, `Auto`)
  - Include Tests / Generated Files / Assets
  - Custom Globs

### 3.6 Output & Notification
- Streams phase updates to the dedicated "AI Context Builder" Output Window pane.
- Uses `IVsInfoBarUIFactory` to show a non-intrusive InfoBar upon completion with action buttons:
  - `Open Markdown`
  - `Copy to Clipboard`

## 4. Packaging and Deployment
- Packaged as a standard `.vsix` targeting Visual Studio 2022 (v17.0+).
- Bundles `AiContextBuilder.Core.dll` and dependencies in the VSIX container.
