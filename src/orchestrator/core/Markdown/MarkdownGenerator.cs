using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using AiPromptContextBuilder.Core.Budget;
using AiPromptContextBuilder.Core.Protocol;

namespace AiPromptContextBuilder.Core.Markdown;

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
    public string? GitScopeDescription { get; set; }
    public string? GitDiffText { get; set; }
    public HashSet<string> ModifiedFiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
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
            ".rs" => "rust",
            ".kt" or ".kts" => "kotlin",
            ".php" => "php",
            ".dart" => "dart",
            ".c" or ".h" => "c",
            ".cpp" or ".cc" or ".cxx" or ".hpp" or ".hh" => "cpp",
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
        string langCode = NormalizeLanguageCode(config.OutputLanguage);
        string? langDisplayName = GetNaturalLanguageDisplayName(langCode);
        string? langNativePrompt = GetLocalizedTaskInstruction(langCode);

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
        if (langDisplayName != null && langNativePrompt != null)
        {
            sb.AppendLine();
            sb.AppendLine($"> **Response Language ({langDisplayName})**: {langNativePrompt}");
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
        if (langDisplayName != null)
        {
            sb.AppendLine($"- Write all explanations, architectural reviews, bug diagnoses, and refactoring notes in **{langDisplayName}** (while keeping code identifiers, syntax, and file paths in their original programming language).");
        }
        sb.AppendLine();

        // 30.4 Scope & Context (Clean AI Prompt Mode)
        if (config.IncludeGenerationMetadata)
        {
            sb.AppendLine("## Target Scope");
            sb.AppendLine();
            sb.AppendLine($"- Root: `{meta.RootPath}`" + (!string.IsNullOrEmpty(meta.RootSymbol) ? $" (Symbol: `{meta.RootSymbol}`)" : ""));
            sb.AppendLine($"- Workspace: `{meta.WorkspaceName}`");
            if (!string.IsNullOrEmpty(meta.GitScopeDescription))
            {
                sb.AppendLine($"- Git Scope: {meta.GitScopeDescription} ({meta.ModifiedFiles.Count} modified files)");
            }
            if (langDisplayName != null)
            {
                sb.AppendLine($"- Response Language: {langDisplayName}");
            }
            if (!string.IsNullOrEmpty(meta.Language) && meta.Language != "Auto")
            {
                sb.AppendLine($"- Language: {meta.Language}");
            }
            if (!string.IsNullOrEmpty(meta.Framework) && meta.Framework != "None" && meta.Framework != "Auto")
            {
                sb.AppendLine($"- Framework: {meta.Framework}");
            }
            if (allExcludedFiles.Count > 0)
            {
                sb.AppendLine($"- Excluded dependencies: {allExcludedFiles.Count} files (see Excluded Files section below)");
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

        // 30.6b Git Unified Diff (when GitScope is active and IncludeGitDiff is true)
        if (config.GitScope != GitScopeMode.None && config.IncludeGitDiff && !string.IsNullOrWhiteSpace(meta.GitDiffText))
        {
            sb.AppendLine("## Git Changes (Unified Diff)");
            sb.AppendLine();
            var (openDiffFence, closeDiffFence) = CreateSafeFence(meta.GitDiffText.Trim(), "diff");
            sb.AppendLine(openDiffFence);
            sb.AppendLine(meta.GitDiffText.Trim());
            sb.AppendLine(closeDiffFence);
            sb.AppendLine();
        }

        // 30.7 Included Files
        sb.AppendLine("## Included Files");
        sb.AppendLine();
        foreach (var f in budgetResult.IncludedFiles)
        {
            bool isMod = config.GitScope != GitScopeMode.None && meta.ModifiedFiles.Contains(f.RelativePath);
            sb.AppendLine(isMod ? $"- `{f.RelativePath}` *(Modified)*" : $"- `{f.RelativePath}`");
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
            bool isMod = config.GitScope != GitScopeMode.None && meta.ModifiedFiles.Contains(f.RelativePath);

            sb.AppendLine(isMod ? $"### File: `{f.RelativePath}` (Modified)" : $"### File: `{f.RelativePath}`");
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

    public static string NormalizeLanguageCode(string? lang)
    {
        if (string.IsNullOrWhiteSpace(lang)) return "en";
        string lower = lang.Trim().ToLowerInvariant();
        return lower switch
        {
            "en" or "en-us" or "english" => "en",
            "es" or "es-es" or "spanish" or "espanol" or "español" => "es",
            "fr" or "fr-fr" or "french" or "francais" or "français" => "fr",
            "de" or "de-de" or "german" or "deutsch" => "de",
            "zh" or "zh-cn" or "chinese" or "中文" or "简体中文" => "zh",
            "ja" or "ja-jp" or "japanese" or "日本語" => "ja",
            "pt" or "pt-br" or "pt-pt" or "portuguese" or "portugues" or "português" => "pt",
            "ru" or "ru-ru" or "russian" or "русский" => "ru",
            "zu" or "zu-za" or "zulu" or "isizulu" => "zu",
            "af" or "af-za" or "afrikaans" => "af",
            _ => "en"
        };
    }

    public static string GetNaturalLanguageDisplayName(string langCode)
    {
        return NormalizeLanguageCode(langCode) switch
        {
            "es" => "🇪🇸 Español (Spanish)",
            "fr" => "🇫🇷 Français (French)",
            "de" => "🇩🇪 Deutsch (German)",
            "zh" => "🇨🇳 简体中文 (Chinese Simplified)",
            "ja" => "🇯🇵 日本語 (Japanese)",
            "pt" => "🇧🇷 Português (Portuguese)",
            "ru" => "🇷🇺 Русский (Russian)",
            "zu" => "🇿🇦 isiZulu (Zulu)",
            "af" => "🇿🇦 Afrikaans",
            _ => "🇺🇸 English (US)"
        };
    }

    public static string GetLocalizedTaskInstruction(string langCode)
    {
        return NormalizeLanguageCode(langCode) switch
        {
            "es" => "Por favor, proporciona todas las explicaciones, revisiones de arquitectura, diagnósticos de errores y comentarios en **Español (Spanish)**, manteniendo los nombres de clases, funciones, variables y rutas de código en su idioma original.",
            "fr" => "Veuillez fournir toutes les explications, analyses architecturales, diagnostics de bogues et commentaires en **Français (French)**, tout en conservant les identifiants de code, noms de classes, fonctions et chemins de fichiers dans leur forme d'origine.",
            "de" => "Bitte verfasse alle Erklärungen, Architektur-Analysen, Fehlerdiagnosen und Kommentare auf **Deutsch (German)**, während Code-Bezeichner, Klassennamen, Funktionen und Dateipfade unverändert im Original bleiben.",
            "zh" => "请使用 **简体中文 (Simplified Chinese)** 提供所有代码解析、架构分析、问题诊断和重构建议；同时请保持代码中的类名、方法名、变量名和文件路径不变。",
            "ja" => "すべての解説、アーキテクチャ分析、バグ診断、および提案は **日本語 (Japanese)** で記述してください。ただし、コード内のクラス名、関数名、変数名、およびファイルパスは元のまま維持してください。",
            "pt" => "Por favor, forneça todas as explicações, análises de arquitetura, diagnósticos de bugs e recomendações em **Português (Portuguese)**, mantendo todos os identificadores de código, nomes de classes, métodos e caminhos de arquivos no original.",
            "ru" => "Пожалуйста, предоставляйте все объяснения, архитектурный анализ, диагностику ошибок и рекомендации на **Русском языке (Russian)**, сохраняя имена классов, методов, переменных и пути к файлам в исходном виде.",
            "zu" => "Sicela unikeze zonke izincazelo, ukuhlaziywa kwesakhiwoikhozi (architecture), ukuxilongwa kwamaphutha (bugs), nezincomo ngolimi lwesi-**isiZulu**, kodwa ugcine amagama amakilasi (classes), imisebenzi (functions/methods), nezindlela zamafayela (file paths) zinjengoba zinjalo kukhodi.",
            "af" => "Verskaf asseblief alle verduidelikings, argitektuur-analises, foutdiagnoses en aanbevelings in **Afrikaans**, terwyl alle kode-identifiseerders, klasname, funksies en lêerpaaie onveranderd in hul oorspronklike vorm behou word.",
            _ => "Provide all explanations and analysis in English."
        };
    }
}
