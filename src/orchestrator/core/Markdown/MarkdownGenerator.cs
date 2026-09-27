using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using AiContextBuilder.Core.Budget;
using AiContextBuilder.Core.Protocol;

namespace AiContextBuilder.Core.Markdown;

public class MarkdownGenerationMetadata
{
    public string RootPath { get; set; } = string.Empty;
    public string? RootSymbol { get; set; }
    public string WorkspaceName { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string Framework { get; set; } = string.Empty;
    public CapabilityLevel AnalysisLevel { get; set; } = CapabilityLevel.Semantic;
    public int FilesDiscovered { get; set; }
    public List<string> Warnings { get; set; } = new();
    public string DependencyTreeText { get; set; } = string.Empty;
}

public static class MarkdownGenerator
{
    public static string GetFenceLanguage(string relativePath)
    {
        string ext = Path.GetExtension(relativePath).ToLowerInvariant();
        return ext switch
        {
            ".cs" => "csharp",
            ".vb" => "vb",
            ".ts" or ".mts" or ".cts" => "typescript",
            ".tsx" => "tsx",
            ".js" or ".mjs" or ".cjs" => "javascript",
            ".jsx" => "jsx",
            ".html" or ".htm" => "html",
            ".css" => "css",
            ".scss" => "scss",
            ".sass" => "sass",
            ".java" => "java",
            ".py" => "python",
            ".go" => "go",
            ".razor" or ".cshtml" => "razor",
            ".vue" => "vue",
            ".json" => "json",
            ".yaml" or ".yml" => "yaml",
            _ => "text"
        };
    }

    public static (string openFence, string closeFence) CreateSafeFence(string content, string lang)
    {
        var matches = Regex.Matches(content, @"`+");
        int maxBackticks = 0;
        foreach (Match m in matches)
        {
            if (m.Length > maxBackticks) maxBackticks = m.Length;
        }

        int fenceLength = Math.Max(3, maxBackticks + 1);
        string fence = new string('`', fenceLength);
        return ($"{fence}{lang}", fence);
    }

    public static string Generate(
        MarkdownGenerationMetadata meta,
        BudgetSelectionResult budgetResult,
        List<EvaluatedFile> allExcludedFiles,
        UserConfiguration config)
    {
        var sb = new StringBuilder();

        // 30.1 Header
        sb.AppendLine("# AI Code Context");
        sb.AppendLine();

        // 30.2 Task
        sb.AppendLine("## Task");
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(config.Task))
        {
            sb.AppendLine(config.Task.Trim());
        }
        else
        {
            sb.AppendLine("Analyse the selected source and its included dependencies. Before proposing changes, identify missing context and unresolved dependencies.");
        }
        sb.AppendLine();

        // 30.3 Context Rules
        sb.AppendLine("## Context Rules");
        sb.AppendLine();
        sb.AppendLine("- Treat included source files as authoritative.");
        sb.AppendLine("- Preserve existing architecture, naming, behaviour, validation, asynchronous behaviour, and error-handling conventions unless the task explicitly requires changes.");
        sb.AppendLine("- Do not invent APIs, symbols, files, or behaviour that are not present in the supplied source.");
        sb.AppendLine("- Identify missing or unresolved context before making assumptions.");
        sb.AppendLine("- When proposing a refactor, provide complete replacement files and preserve their original paths.");
        sb.AppendLine("- Do not shorten implementations with placeholders, omitted regions, or ellipses.");
        sb.AppendLine("- Account for the dependency relationships and analysis limitations documented below.");
        sb.AppendLine();

        // 30.4 Generation Details
        if (config.IncludeGenerationMetadata)
        {
            sb.AppendLine("## Generation Details");
            sb.AppendLine();
            sb.AppendLine($"- Root path: `{meta.RootPath}`");
            if (!string.IsNullOrEmpty(meta.RootSymbol))
            {
                sb.AppendLine($"- Root symbol: `{meta.RootSymbol}`");
            }
            sb.AppendLine($"- Workspace: `{meta.WorkspaceName}`");
            sb.AppendLine($"- Language: {meta.Language}");
            sb.AppendLine($"- Framework: {meta.Framework}");
            sb.AppendLine($"- Analysis capability: {meta.AnalysisLevel}");
            sb.AppendLine($"- Files discovered: {meta.FilesDiscovered}");
            sb.AppendLine($"- Files included: {budgetResult.IncludedFiles.Count}");
            sb.AppendLine($"- Files excluded: {allExcludedFiles.Count}");
            sb.AppendLine($"- Maximum depth: {(config.MaxDepth.HasValue ? config.MaxDepth.Value.ToString() : "Unlimited")}");
            sb.AppendLine($"- Token budget: {(config.MaxTokens.HasValue ? config.MaxTokens.Value.ToString("N0") : "Unlimited")}");
            sb.AppendLine($"- Token estimator: {config.Tokenizer}");
            sb.AppendLine($"- Estimated full tokens: {budgetResult.FullEstimatedTokens:N0}");
            sb.AppendLine($"- Estimated final tokens: {budgetResult.FinalEstimatedTokens:N0}");
            sb.AppendLine($"- Comment mode requested: {config.CommentMode}");
            sb.AppendLine($"- Comment policy applied: {budgetResult.CommentsApplied}");
            sb.AppendLine($"- Tests included: {config.IncludeTests}");
            sb.AppendLine($"- Generated files included: {config.IncludeGeneratedFiles}");
            sb.AppendLine($"- Companion files included: {config.IncludeCompanionFiles}");
            sb.AppendLine($"- Assets included: {config.IncludeAssets}");
            if (!config.OmitTimestamp)
            {
                sb.AppendLine($"- Generation timestamp: {DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}");
            }
            else
            {
                sb.AppendLine("- Generation timestamp: [Omitted for deterministic testing]");
            }
            sb.AppendLine();
        }

        // 30.5 Analysis Warnings
        if (meta.Warnings.Count > 0 || budgetResult.RootAloneExceedsBudget)
        {
            sb.AppendLine("## Analysis Warnings");
            sb.AppendLine();
            if (budgetResult.RootAloneExceedsBudget)
            {
                sb.AppendLine($"- Warning: Root file alone exceeds configured token budget ({config.MaxTokens}). Root file was retained in full.");
            }
            foreach (var w in meta.Warnings)
            {
                sb.AppendLine($"- {w}");
            }
            sb.AppendLine();
        }

        // 30.6 Dependency Tree
        if (config.IncludeDependencyTree)
        {
            sb.AppendLine("## Dependency Tree");
            sb.AppendLine();
            sb.AppendLine("```text");
            sb.AppendLine(meta.DependencyTreeText.Trim());
            sb.AppendLine("```");
            sb.AppendLine();
        }

        // 30.7 Included Files
        sb.AppendLine("## Included Files");
        sb.AppendLine();
        foreach (var f in budgetResult.IncludedFiles)
        {
            sb.AppendLine($"- `{f.RelativePath}`");
        }
        sb.AppendLine();

        // 30.8 Excluded Files
        if (config.IncludeExcludedFileList && allExcludedFiles.Count > 0)
        {
            sb.AppendLine("## Excluded Files");
            sb.AppendLine();
            sb.AppendLine("| Relative Path | Reason | Depth | Importance | Source Available |");
            sb.AppendLine("| --- | --- | --- | --- | --- |");
            foreach (var f in allExcludedFiles)
            {
                string reason = f.ExclusionReason.HasValue ? f.ExclusionReason.Value.ToString() : "TokenBudget";
                string depth = f.Depth >= 0 ? f.Depth.ToString() : "-";
                string score = f.Importance >= 0 ? f.Importance.ToString() : "-";
                string src = !string.IsNullOrEmpty(f.OriginalContent) ? "Yes" : "No";
                sb.AppendLine($"| `{f.RelativePath}` | {reason} | {depth} | {score} | {src} |");
            }
            sb.AppendLine();
        }

        // 30.9 Source Files
        sb.AppendLine("## Source Files");
        sb.AppendLine();
        foreach (var f in budgetResult.IncludedFiles)
        {
            string lang = GetFenceLanguage(f.RelativePath);
            var (openFence, closeFence) = CreateSafeFence(f.ProcessedContent, lang);

            sb.AppendLine($"### File: `{f.RelativePath}`");
            sb.AppendLine();
            sb.AppendLine(openFence);
            sb.AppendLine(f.ProcessedContent);
            sb.AppendLine(closeFence);
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd() + Environment.NewLine;
    }

    public static string GetDefaultOutputFilename(string rootPath)
    {
        string baseName = Path.GetFileNameWithoutExtension(rootPath);
        string kebab = Regex.Replace(baseName, @"([a-z0-9])([A-Z])", "$1-$2")
                            .Replace('_', '-')
                            .Replace('.', '-')
                            .ToLowerInvariant();
        return $"{kebab}-context.md";
    }
}
