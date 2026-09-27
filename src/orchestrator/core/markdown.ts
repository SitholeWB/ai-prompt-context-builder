import * as path from 'node:path';
import { EvaluatedFile, BudgetSelectionResult } from './budget.js';
import {
  UserConfiguration,
  CapabilityLevel,
  ExclusionReason,
} from '../protocol/types.js';

export interface MarkdownGenerationMetadata {
  rootPath: string;
  rootSymbol?: string;
  workspaceName: string;
  language: string;
  framework: string;
  analysisLevel: CapabilityLevel;
  filesDiscovered: number;
  warnings: string[];
  dependencyTreeText: string;
}

export function getFenceLanguage(relativePath: string): string {
  const ext = path.extname(relativePath).toLowerCase();
  switch (ext) {
    case '.cs':
      return 'csharp';
    case '.vb':
      return 'vb';
    case '.ts':
      return 'typescript';
    case '.mts':
    case '.cts':
      return 'typescript';
    case '.tsx':
      return 'tsx';
    case '.js':
    case '.mjs':
    case '.cjs':
      return 'javascript';
    case '.jsx':
      return 'jsx';
    case '.html':
    case '.htm':
      return 'html';
    case '.css':
      return 'css';
    case '.scss':
      return 'scss';
    case '.sass':
      return 'sass';
    case '.java':
      return 'java';
    case '.py':
      return 'python';
    case '.go':
      return 'go';
    case '.razor':
    case '.cshtml':
      return 'razor';
    case '.vue':
      return 'vue';
    case '.json':
      return 'json';
    case '.yaml':
    case '.yml':
      return 'yaml';
    default:
      return 'text';
  }
}

/**
 * Creates safe Markdown code fence:
 * Detects the longest run of backticks in source and makes the outer fence longer.
 */
export function createSafeFence(content: string, lang: string): { openFence: string; closeFence: string } {
  const matches = content.match(/`+/g);
  let maxBackticks = 0;
  if (matches) {
    for (const m of matches) {
      if (m.length > maxBackticks) {
        maxBackticks = m.length;
      }
    }
  }

  const fenceLength = Math.max(3, maxBackticks + 1);
  const fence = '`'.repeat(fenceLength);
  return {
    openFence: `${fence}${lang}`,
    closeFence: fence,
  };
}

export function generateMarkdownOutput(
  meta: MarkdownGenerationMetadata,
  budgetResult: BudgetSelectionResult,
  allExcludedFiles: EvaluatedFile[],
  config: UserConfiguration
): string {
  const parts: string[] = [];

  // 30.1 Header
  parts.push('# AI Code Context');
  parts.push('');

  // 30.2 Task
  parts.push('## Task');
  parts.push('');
  if (config.task && config.task.trim().length > 0) {
    parts.push(config.task.trim());
  } else {
    parts.push(
      'Analyse the selected source and its included dependencies. Before proposing changes, identify missing context and unresolved dependencies.'
    );
  }
  parts.push('');

  // 30.3 Context Rules
  parts.push('## Context Rules');
  parts.push('');
  parts.push('- Treat included source files as authoritative.');
  parts.push(
    '- Preserve existing architecture, naming, behaviour, validation, asynchronous behaviour, and error-handling conventions unless the task explicitly requires changes.'
  );
  parts.push('- Do not invent APIs, symbols, files, or behaviour that are not present in the supplied source.');
  parts.push('- Identify missing or unresolved context before making assumptions.');
  parts.push('- When proposing a refactor, provide complete replacement files and preserve their original paths.');
  parts.push('- Do not shorten implementations with placeholders, omitted regions, or ellipses.');
  parts.push('- Account for the dependency relationships and analysis limitations documented below.');
  parts.push('');

  // 30.4 Generation Details
  if (config.includeGenerationMetadata) {
    parts.push('## Generation Details');
    parts.push('');
    parts.push(`- Root path: \`${meta.rootPath}\``);
    if (meta.rootSymbol) {
      parts.push(`- Root symbol: \`${meta.rootSymbol}\``);
    }
    parts.push(`- Workspace: \`${meta.workspaceName}\``);
    parts.push(`- Language: ${meta.language}`);
    parts.push(`- Framework: ${meta.framework}`);
    parts.push(`- Analysis capability: ${meta.analysisLevel}`);
    parts.push(`- Files discovered: ${meta.filesDiscovered}`);
    parts.push(`- Files included: ${budgetResult.includedFiles.length}`);
    parts.push(`- Files excluded: ${allExcludedFiles.length}`);
    parts.push(
      `- Maximum depth: ${config.maxDepth !== null && config.maxDepth !== undefined ? config.maxDepth : 'Unlimited'}`
    );
    parts.push(
      `- Token budget: ${config.maxTokens !== null && config.maxTokens !== undefined ? config.maxTokens.toLocaleString() : 'Unlimited'}`
    );
    parts.push(`- Token estimator: ${config.tokenizer}`);
    parts.push(`- Estimated full tokens: ${budgetResult.fullEstimatedTokens.toLocaleString()}`);
    parts.push(`- Estimated final tokens: ${budgetResult.finalEstimatedTokens.toLocaleString()}`);
    parts.push(`- Comment mode requested: ${config.commentMode}`);
    parts.push(`- Comment policy applied: ${budgetResult.commentsApplied}`);
    parts.push(`- Tests included: ${config.includeTests}`);
    parts.push(`- Generated files included: ${config.includeGeneratedFiles}`);
    parts.push(`- Companion files included: ${config.includeCompanionFiles}`);
    parts.push(`- Assets included: ${config.includeAssets}`);
    if (!config.omitTimestamp) {
      parts.push(`- Generation timestamp: ${new Date().toISOString()}`);
    } else {
      parts.push(`- Generation timestamp: [Omitted for deterministic testing]`);
    }
    parts.push('');
  }

  // 30.5 Analysis Warnings
  if (meta.warnings.length > 0 || budgetResult.rootAloneExceedsBudget) {
    parts.push('## Analysis Warnings');
    parts.push('');
    if (budgetResult.rootAloneExceedsBudget) {
      parts.push(
        `- Warning: Root file alone exceeds configured token budget (${config.maxTokens}). Root file was retained in full.`
      );
    }
    for (const w of meta.warnings) {
      parts.push(`- ${w}`);
    }
    parts.push('');
  }

  // 30.6 Dependency Tree
  if (config.includeDependencyTree) {
    parts.push('## Dependency Tree');
    parts.push('');
    parts.push('```text');
    parts.push(meta.dependencyTreeText.trim());
    parts.push('```');
    parts.push('');
  }

  // 30.7 Included Files
  parts.push('## Included Files');
  parts.push('');
  for (const f of budgetResult.includedFiles) {
    parts.push(`- \`${f.relativePath}\``);
  }
  parts.push('');

  // 30.8 Excluded Files
  if (config.includeExcludedFileList && allExcludedFiles.length > 0) {
    parts.push('## Excluded Files');
    parts.push('');
    parts.push('| Relative Path | Reason | Depth | Importance | Source Available |');
    parts.push('| --- | --- | --- | --- | --- |');
    for (const f of allExcludedFiles) {
      const reason = f.exclusionReason || 'TokenBudget';
      const depth = f.depth >= 0 ? f.depth : '-';
      const score = f.importance >= 0 ? f.importance : '-';
      const src = f.originalContent ? 'Yes' : 'No';
      parts.push(`| \`${f.relativePath}\` | ${reason} | ${depth} | ${score} | ${src} |`);
    }
    parts.push('');
  }

  // 30.9 Source Files
  parts.push('## Source Files');
  parts.push('');
  for (const f of budgetResult.includedFiles) {
    const lang = getFenceLanguage(f.relativePath);
    const { openFence, closeFence } = createSafeFence(f.processedContent, lang);

    parts.push(`### File: \`${f.relativePath}\``);
    parts.push('');
    parts.push(openFence);
    parts.push(f.processedContent);
    parts.push(closeFence);
    parts.push('');
  }

  return parts.join('\n');
}

export function getDefaultOutputFilename(rootPath: string): string {
  const base = path.basename(rootPath);
  const ext = path.extname(base);
  const nameWithoutExt = ext ? base.slice(0, -ext.length) : base;

  // Convert PascalCase or camelCase to kebab-case
  const kebab = nameWithoutExt
    .replace(/([a-z0-9])([A-Z])/g, '$1-$2')
    .replace(/[\s_.]+/g, '-')
    .toLowerCase();

  return `${kebab}-context.md`;
}
