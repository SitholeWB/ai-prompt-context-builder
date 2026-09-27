import { DependencyGraph } from './graph.js';
import {
  GraphNode,
  ExclusionReason,
  UserConfiguration,
  CommentMode,
} from '../protocol/types.js';
import { ITokenEstimator } from './tokenizer.js';
import { removeComments } from './comments.js';
import * as path from 'node:path';

export interface EvaluatedFile {
  relativePath: string;
  absolutePath: string;
  nodes: GraphNode[];
  isRoot: boolean;
  depth: number;
  importance: number;
  originalContent: string;
  processedContent: string;
  tokenCount: number;
  included: boolean;
  exclusionReason?: ExclusionReason;
  isCompanion: boolean;
  parentFilePaths: string[];
}

export interface BudgetSelectionResult {
  includedFiles: EvaluatedFile[];
  excludedFiles: EvaluatedFile[];
  fullEstimatedTokens: number;
  finalEstimatedTokens: number;
  commentsApplied: 'Preserved' | 'Removed';
  budgetMet: boolean;
  rootAloneExceedsBudget: boolean;
}

export function performTokenBudgetSelection(
  candidateFiles: EvaluatedFile[],
  graph: DependencyGraph,
  config: UserConfiguration,
  tokenEstimator: ITokenEstimator,
  markdownOverheadTokens = 500
): BudgetSelectionResult {
  const rootFile = candidateFiles.find((f) => f.isRoot);
  if (!rootFile) {
    throw new Error('No root file present in candidate list.');
  }

  // Calculate full preserved tokens across all candidate files
  let totalPreservedFileTokens = 0;
  for (const f of candidateFiles) {
    totalPreservedFileTokens += tokenEstimator.estimateTokens(f.originalContent).tokenCount;
  }
  const fullEstimatedTokens = totalPreservedFileTokens + markdownOverheadTokens;

  // Case 1: No token budget specified (Section 26)
  if (config.maxTokens === null || config.maxTokens === undefined) {
    const commentsApplied = config.commentMode === 'Remove' ? 'Removed' : 'Preserved';
    for (const f of candidateFiles) {
      if (commentsApplied === 'Removed') {
        const ext = path.extname(f.relativePath);
        f.processedContent = removeComments(f.originalContent, ext, config.language).code;
      } else {
        f.processedContent = f.originalContent;
      }
      f.tokenCount = tokenEstimator.estimateTokens(f.processedContent).tokenCount;
      f.included = true;
    }

    let finalFileTokens = 0;
    for (const f of candidateFiles) {
      finalFileTokens += f.tokenCount;
    }

    return {
      includedFiles: candidateFiles,
      excludedFiles: [],
      fullEstimatedTokens,
      finalEstimatedTokens: finalFileTokens + markdownOverheadTokens,
      commentsApplied,
      budgetMet: true,
      rootAloneExceedsBudget: false,
    };
  }

  // Case 2: Token budget specified (Section 29)
  const budget = config.maxTokens;
  const availableBudgetForFiles = Math.max(100, budget - markdownOverheadTokens);

  // Check root file token count
  const rootPreservedTokens = tokenEstimator.estimateTokens(rootFile.originalContent).tokenCount;
  const ext = path.extname(rootFile.relativePath);
  const rootRemovedTokens = tokenEstimator.estimateTokens(
    removeComments(rootFile.originalContent, ext, config.language).code
  ).tokenCount;

  // Decide comment policy under budget
  let applyCommentRemoval = config.commentMode === 'Remove';
  if (config.commentMode === 'Auto') {
    // If full preserved candidate set exceeds budget, attempt comment removal first
    if (fullEstimatedTokens > budget) {
      applyCommentRemoval = true;
    }
  }

  // Prepare processed contents and token counts
  for (const f of candidateFiles) {
    if (applyCommentRemoval) {
      const fileExt = path.extname(f.relativePath);
      f.processedContent = removeComments(f.originalContent, fileExt, config.language).code;
    } else {
      f.processedContent = f.originalContent;
    }
    f.tokenCount = tokenEstimator.estimateTokens(f.processedContent).tokenCount;
  }

  // Check if root file alone exceeds budget
  const rootTokens = rootFile.tokenCount;
  const rootAloneExceedsBudget = rootTokens > availableBudgetForFiles;

  // Root is ALWAYS included
  rootFile.included = true;
  let accumulatedTokens = rootTokens;

  // Rank candidate non-root files deterministically:
  // Companions first, then by depth ascending, then importance descending, then relativePath ascending
  const nonRootFiles = candidateFiles.filter((f) => !f.isRoot);
  nonRootFiles.sort((a, b) => {
    if (a.isCompanion !== b.isCompanion) {
      return a.isCompanion ? -1 : 1;
    }
    if (a.depth !== b.depth) {
      return a.depth - b.depth;
    }
    if (a.importance !== b.importance) {
      return b.importance - a.importance;
    }
    return a.relativePath.localeCompare(b.relativePath);
  });

  const includedSet = new Set<string>();
  includedSet.add(rootFile.relativePath);

  for (const f of nonRootFiles) {
    // Check coherent dependency chain: must have at least one parent file already included
    const hasIncludedParent =
      f.parentFilePaths.length === 0 ||
      f.parentFilePaths.some((p) => includedSet.has(p));

    if (!hasIncludedParent) {
      f.included = false;
      f.exclusionReason = 'TokenBudget'; // excluded to keep chain coherent
      continue;
    }

    if (accumulatedTokens + f.tokenCount <= availableBudgetForFiles) {
      f.included = true;
      includedSet.add(f.relativePath);
      accumulatedTokens += f.tokenCount;
    } else {
      f.included = false;
      f.exclusionReason = 'TokenBudget';
    }
  }

  const includedFiles = candidateFiles.filter((f) => f.included);
  const excludedFiles = candidateFiles.filter((f) => !f.included);
  const finalEstimatedTokens = accumulatedTokens + markdownOverheadTokens;

  return {
    includedFiles,
    excludedFiles,
    fullEstimatedTokens,
    finalEstimatedTokens,
    commentsApplied: applyCommentRemoval ? 'Removed' : 'Preserved',
    budgetMet: finalEstimatedTokens <= budget || rootAloneExceedsBudget,
    rootAloneExceedsBudget,
  };
}
