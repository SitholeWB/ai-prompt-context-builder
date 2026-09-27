using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AiPromptContextBuilder.Core.Comments;
using AiPromptContextBuilder.Core.Graph;
using AiPromptContextBuilder.Core.Protocol;
using AiPromptContextBuilder.Core.Tokenizer;

namespace AiPromptContextBuilder.Core.Budget;

public class EvaluatedFile
{
    public string RelativePath { get; set; } = string.Empty;
    public string AbsolutePath { get; set; } = string.Empty;
    public List<GraphNode> Nodes { get; set; } = new();
    public bool IsRoot { get; set; }
    public int Depth { get; set; }
    public int Importance { get; set; }
    public string OriginalContent { get; set; } = string.Empty;
    public string ProcessedContent { get; set; } = string.Empty;
    public int TokenCount { get; set; }
    public bool Included { get; set; }
    public ExclusionReason? ExclusionReason { get; set; }
    public bool IsCompanion { get; set; }
    public List<string> ParentFilePaths { get; set; } = new();
}

public class BudgetSelectionResult
{
    public List<EvaluatedFile> IncludedFiles { get; set; } = new();
    public List<EvaluatedFile> ExcludedFiles { get; set; } = new();
    public int FullEstimatedTokens { get; set; }
    public int FinalEstimatedTokens { get; set; }
    public string CommentsApplied { get; set; } = "Preserved";
    public bool BudgetMet { get; set; }
    public bool RootAloneExceedsBudget { get; set; }
}

public static class TokenBudgetSelector
{
    public static BudgetSelectionResult SelectFiles(
        List<EvaluatedFile> candidateFiles,
        DependencyGraph graph,
        UserConfiguration config,
        ITokenEstimator tokenEstimator,
        int markdownOverheadTokens = 500)
    {
        var rootFile = candidateFiles.FirstOrDefault(f => f.IsRoot)
            ?? throw new InvalidOperationException("No root file present in candidate list.");

        // Estimate full preserved tokens
        int totalPreservedFileTokens = candidateFiles.Sum(f => tokenEstimator.EstimateTokens(f.OriginalContent).TokenCount);
        int fullEstimatedTokens = totalPreservedFileTokens + markdownOverheadTokens;

        // Case 1: No token budget specified (Section 26)
        if (!config.MaxTokens.HasValue)
        {
            string commentsApplied = config.CommentMode == CommentMode.Remove ? "Removed" : "Preserved";
            foreach (var f in candidateFiles)
            {
                if (commentsApplied == "Removed")
                {
                    string ext = Path.GetExtension(f.RelativePath);
                    f.ProcessedContent = CommentHandler.RemoveComments(f.OriginalContent, ext, config.Language).Code;
                }
                else
                {
                    f.ProcessedContent = f.OriginalContent;
                }
                f.TokenCount = tokenEstimator.EstimateTokens(f.ProcessedContent).TokenCount;
                f.Included = true;
            }

            int finalTokens = candidateFiles.Sum(f => f.TokenCount) + markdownOverheadTokens;
            return new BudgetSelectionResult
            {
                IncludedFiles = candidateFiles,
                ExcludedFiles = new List<EvaluatedFile>(),
                FullEstimatedTokens = fullEstimatedTokens,
                FinalEstimatedTokens = finalTokens,
                CommentsApplied = commentsApplied,
                BudgetMet = true,
                RootAloneExceedsBudget = false
            };
        }

        // Case 2: Token budget specified (Section 29)
        int budget = config.MaxTokens.Value;
        int availableBudgetForFiles = Math.Max(100, budget - markdownOverheadTokens);

        bool applyCommentRemoval = config.CommentMode == CommentMode.Remove;
        if (config.CommentMode == CommentMode.Auto)
        {
            if (fullEstimatedTokens > budget)
            {
                applyCommentRemoval = true;
            }
        }

        foreach (var f in candidateFiles)
        {
            if (applyCommentRemoval)
            {
                string ext = Path.GetExtension(f.RelativePath);
                f.ProcessedContent = CommentHandler.RemoveComments(f.OriginalContent, ext, config.Language).Code;
            }
            else
            {
                f.ProcessedContent = f.OriginalContent;
            }
            f.TokenCount = tokenEstimator.EstimateTokens(f.ProcessedContent).TokenCount;
        }

        bool rootAloneExceedsBudget = rootFile.TokenCount > availableBudgetForFiles;

        // Root file is ALWAYS included
        rootFile.Included = true;
        int accumulatedTokens = rootFile.TokenCount;

        var nonRootFiles = candidateFiles.Where(f => !f.IsRoot).ToList();
        nonRootFiles.Sort((a, b) =>
        {
            if (a.IsCompanion != b.IsCompanion)
                return a.IsCompanion ? -1 : 1;
            if (a.Depth != b.Depth)
                return a.Depth.CompareTo(b.Depth);
            if (a.Importance != b.Importance)
                return b.Importance.CompareTo(a.Importance);
            return string.Compare(a.RelativePath, b.RelativePath, StringComparison.OrdinalIgnoreCase);
        });

        var includedSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { rootFile.RelativePath };

        foreach (var f in nonRootFiles)
        {
            // Coherent chain check: must have at least one parent file already included
            bool hasIncludedParent = f.ParentFilePaths.Count == 0 ||
                                     f.ParentFilePaths.Any(p => includedSet.Contains(p));

            if (!hasIncludedParent)
            {
                f.Included = false;
                f.ExclusionReason = ExclusionReason.TokenBudget;
                continue;
            }

            if (accumulatedTokens + f.TokenCount <= availableBudgetForFiles)
            {
                f.Included = true;
                includedSet.Add(f.RelativePath);
                accumulatedTokens += f.TokenCount;
            }
            else
            {
                f.Included = false;
                f.ExclusionReason = ExclusionReason.TokenBudget;
            }
        }

        var includedFiles = candidateFiles.Where(f => f.Included).ToList();
        var excludedFiles = candidateFiles.Where(f => !f.Included).ToList();
        int finalEstimatedTokens = accumulatedTokens + markdownOverheadTokens;

        return new BudgetSelectionResult
        {
            IncludedFiles = includedFiles,
            ExcludedFiles = excludedFiles,
            FullEstimatedTokens = fullEstimatedTokens,
            FinalEstimatedTokens = finalEstimatedTokens,
            CommentsApplied = applyCommentRemoval ? "Removed" : "Preserved",
            BudgetMet = finalEstimatedTokens <= budget || rootAloneExceedsBudget,
            RootAloneExceedsBudget = rootAloneExceedsBudget
        };
    }
}
