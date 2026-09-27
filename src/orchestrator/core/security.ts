import * as path from 'node:path';
import * as fs from 'node:fs';
import { SecretFinding } from '../protocol/types.js';

export interface SecretPattern {
  category: string;
  regex: RegExp;
  recommendation: string;
}

export const SECRET_PATTERNS: SecretPattern[] = [
  {
    category: 'Private Key',
    regex: /-----BEGIN (?:RSA |EC |DSA |OPENSSH )?PRIVATE KEY-----/,
    recommendation: 'Remove private key or use an external secret management store.',
  },
  {
    category: 'AWS Access Key',
    regex: /\b(AKIA[0-9A-Z]{16})\b/,
    recommendation: 'Rotate AWS access key and store credentials in environment or AWS Secrets Manager.',
  },
  {
    category: 'AWS Secret Access Key',
    regex: /(?:aws_secret_access_key|AWS_SECRET_ACCESS_KEY)\s*[:=]\s*['"][A-Za-z0-9\/+=]{40}['"]/,
    recommendation: 'Store AWS secrets outside repository files.',
  },
  {
    category: 'GitHub Token',
    regex: /\b(?:ghp|gho|ghu|ghs|ghr)_[A-Za-z0-9_]{36,255}\b/,
    recommendation: 'Revoke and rotate GitHub personal access token.',
  },
  {
    category: 'Generic API Key Assignment',
    regex: /(?:api[_-]?key|apikey|secret[_-]?key|auth[_-]?token)\s*[:=]\s*['"][A-Za-z0-9_\-]{16,128}['"]/i,
    recommendation: 'Move API keys and tokens to environment variables or secret vaults.',
  },
  {
    category: 'Connection String Password',
    regex: /(?:password|pwd)\s*=\s*['"]?[^;\s'"]{4,}['"]?(?=.*(?:server|data source|host|uid|user id))/i,
    recommendation: 'Use managed identities or secure connection strings without embedded passwords.',
  },
  {
    category: 'Database URI with Password',
    regex: /(?:postgres|postgresql|mysql|mongodb|redis):\/\/[^:\/\s]+:[^@\/\s]+@[^:\/\s]+/i,
    recommendation: 'Omit database credentials from URI and configure them at deployment.',
  },
  {
    category: 'Slack Token',
    regex: /\bxox[baprs]-[0-9]{10,13}-[0-9]{10,13}[a-zA-Z0-9-]*\b/,
    recommendation: 'Revoke Slack token and load dynamically.',
  },
];

/**
 * Scans file content for sensitive secrets without ever exposing the secret values.
 */
export function scanForSecrets(relativePath: string, content: string): SecretFinding[] {
  const findings: SecretFinding[] = [];
  const lines = content.split(/\r?\n/);

  for (let lineIndex = 0; lineIndex < lines.length; lineIndex++) {
    const line = lines[lineIndex];
    for (const pattern of SECRET_PATTERNS) {
      if (pattern.regex.test(line)) {
        findings.push({
          relativePath,
          line: lineIndex + 1,
          category: pattern.category,
          recommendation: pattern.recommendation,
        });
        break; // One finding per line is sufficient
      }
    }
  }

  return findings;
}

/**
 * Validates that target path is canonical, exists, and is strictly within workspace boundary.
 * Prevents directory traversal attacks via symlinks or relative sequences ('..').
 */
export function validatePathInWorkspace(targetPath: string, workspaceRoot: string): string {
  const resolvedWorkspace = path.resolve(workspaceRoot);
  let resolvedTarget: string;

  try {
    // If symlink, resolve real path
    if (fs.existsSync(targetPath)) {
      resolvedTarget = fs.realpathSync(targetPath);
    } else {
      resolvedTarget = path.resolve(targetPath);
    }
  } catch {
    resolvedTarget = path.resolve(targetPath);
  }

  // Canonical workspace path
  const canonicalWorkspace = fs.existsSync(resolvedWorkspace)
    ? fs.realpathSync(resolvedWorkspace)
    : resolvedWorkspace;

  // Strict check: target must be inside workspace or equal to workspace
  const rel = path.relative(canonicalWorkspace, resolvedTarget);
  if (rel.startsWith('..') || path.isAbsolute(rel)) {
    throw new Error(
      `Security violation: Path "${targetPath}" resolves to "${resolvedTarget}", which is outside workspace "${canonicalWorkspace}".`
    );
  }

  // Block .git internal inspection
  const pathParts = rel.split(path.sep);
  if (pathParts.includes('.git')) {
    throw new Error(`Security violation: Reading from .git internal directories is prohibited.`);
  }

  return resolvedTarget;
}
