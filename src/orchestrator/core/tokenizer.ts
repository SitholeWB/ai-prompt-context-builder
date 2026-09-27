export interface TokenEstimationResult {
  tokenCount: number;
  isEstimated: boolean;
  estimatorName: string;
  accuracyLevel: 'Exact' | 'HighConfidence' | 'Estimated';
}

export interface ITokenEstimator {
  readonly name: string;
  readonly accuracyLevel: 'Exact' | 'HighConfidence' | 'Estimated';
  estimateTokens(text: string): TokenEstimationResult;
}

/**
 * ApproximateTokenEstimator
 *
 * Provides a fast, deterministic, local token estimation based on character and
 * token-boundary analysis without requiring network access or external binary models.
 * For code, programming keywords, identifiers, and markdown formatting, average
 * characters-per-token typically ranges from 3.5 to 4.0 chars/token.
 */
export class ApproximateTokenEstimator implements ITokenEstimator {
  public readonly name = 'Approximate Token Estimator (Local Heuristic)';
  public readonly accuracyLevel: 'Estimated' = 'Estimated';

  public estimateTokens(text: string): TokenEstimationResult {
    if (!text || text.length === 0) {
      return {
        tokenCount: 0,
        isEstimated: true,
        estimatorName: this.name,
        accuracyLevel: this.accuracyLevel,
      };
    }

    // Heuristic:
    // 1. Whitespace and punctuation splits
    // 2. Word boundary adjustments for camelCase and PascalCase
    // 3. Average fallback for code: ~3.7 chars per token
    const wordsAndTokens = text.match(/[A-Z]?[a-z]+|[A-Z]+(?=[A-Z][a-z]|\b)|[0-9]+|[^\s\w]/g);
    let count = 0;
    if (wordsAndTokens && wordsAndTokens.length > 0) {
      count = Math.ceil(wordsAndTokens.length * 1.05);
    } else {
      count = Math.ceil(text.length / 3.7);
    }

    return {
      tokenCount: Math.max(1, count),
      isEstimated: true,
      estimatorName: this.name,
      accuracyLevel: this.accuracyLevel,
    };
  }
}
