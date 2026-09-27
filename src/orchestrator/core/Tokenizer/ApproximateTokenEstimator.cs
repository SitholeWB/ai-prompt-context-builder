using System;
using System.Text.RegularExpressions;

namespace AiPromptContextBuilder.Core.Tokenizer;

public class TokenEstimationResult
{
    public int TokenCount { get; set; }
    public bool IsEstimated { get; set; } = true;
    public string EstimatorName { get; set; } = string.Empty;
    public string AccuracyLevel { get; set; } = "Estimated"; // Exact, HighConfidence, Estimated
}

public interface ITokenEstimator
{
    string Name { get; }
    string AccuracyLevel { get; }
    TokenEstimationResult EstimateTokens(string text);
}

public class ApproximateTokenEstimator : ITokenEstimator
{
    public string Name => "Approximate Token Estimator (Local Heuristic)";
    public string AccuracyLevel => "Estimated";

    private static readonly Regex TokenRegex = new(
        @"[A-Z]?[a-z]+|[A-Z]+(?=[A-Z][a-z]|\b)|[0-9]+|[^\s\w]",
        RegexOptions.Compiled);

    public TokenEstimationResult EstimateTokens(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return new TokenEstimationResult
            {
                TokenCount = 0,
                IsEstimated = true,
                EstimatorName = Name,
                AccuracyLevel = AccuracyLevel
            };
        }

        var matches = TokenRegex.Matches(text);
        int count = matches.Count > 0
            ? (int)Math.Ceiling(matches.Count * 1.05)
            : (int)Math.Ceiling(text.Length / 3.7);

        return new TokenEstimationResult
        {
            TokenCount = Math.Max(1, count),
            IsEstimated = true,
            EstimatorName = Name,
            AccuracyLevel = AccuracyLevel
        };
    }
}
