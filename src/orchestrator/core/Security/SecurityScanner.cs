using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using AiContextBuilder.Core.Protocol;

namespace AiContextBuilder.Core.Security;

public class SecretPattern
{
    public string Category { get; set; } = string.Empty;
    public Regex Regex { get; set; } = null!;
    public string Recommendation { get; set; } = string.Empty;
}

public static class SecurityScanner
{
    private static readonly List<SecretPattern> Patterns = new()
    {
        new SecretPattern
        {
            Category = "Private Key",
            Regex = new Regex(@"-----BEGIN (?:RSA |EC |DSA |OPENSSH )?PRIVATE KEY-----", RegexOptions.Compiled),
            Recommendation = "Store private keys in a secure key vault or environment variable."
        },
        new SecretPattern
        {
            Category = "AWS Access Key",
            Regex = new Regex(@"\b(AKIA[0-9A-Z]{16})\b", RegexOptions.Compiled),
            Recommendation = "Rotate AWS access key and store credentials in AWS Secrets Manager or IAM role."
        },
        new SecretPattern
        {
            Category = "AWS Secret Access Key",
            Regex = new Regex(@"(?:aws_secret_access_key|AWS_SECRET_ACCESS_KEY)\s*[:=]\s*['""][A-Za-z0-9\/+=]{40}['""]", RegexOptions.Compiled),
            Recommendation = "Store AWS secrets outside repository files."
        },
        new SecretPattern
        {
            Category = "GitHub Token",
            Regex = new Regex(@"\b(?:ghp|gho|ghu|ghs|ghr)_[A-Za-z0-9_]{36,255}\b", RegexOptions.Compiled),
            Recommendation = "Revoke and rotate GitHub personal access token."
        },
        new SecretPattern
        {
            Category = "Generic API Key Assignment",
            Regex = new Regex(@"(?:api[_-]?key|apikey|secret[_-]?key|auth[_-]?token)\s*[:=]\s*['""][A-Za-z0-9_\-]{16,128}['""]", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            Recommendation = "Move API keys and tokens to environment variables or secret vaults."
        },
        new SecretPattern
        {
            Category = "Connection String Password",
            Regex = new Regex(@"(?:password|pwd)\s*=\s*['""]?[^;\s'""]{4,}['""]?(?=.*(?:server|data source|host|uid|user id))", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            Recommendation = "Use managed identities or secure connection strings without embedded passwords."
        },
        new SecretPattern
        {
            Category = "Database URI with Password",
            Regex = new Regex(@"(?:postgres|postgresql|mysql|mongodb|redis):\/\/[^:\/\s]+:[^@\/\s]+@[^:\/\s]+", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            Recommendation = "Omit database credentials from URI and configure them at deployment."
        },
        new SecretPattern
        {
            Category = "Slack Token",
            Regex = new Regex(@"\bxox[baprs]-[0-9]{10,13}-[0-9]{10,13}[a-zA-Z0-9-]*\b", RegexOptions.Compiled),
            Recommendation = "Revoke Slack token and load dynamically."
        }
    };

    public static List<SecretFinding> ScanForSecrets(string relativePath, string content)
    {
        var findings = new List<SecretFinding>();
        using var reader = new StringReader(content);
        string? line;
        int lineNumber = 0;

        while ((line = reader.ReadLine()) != null)
        {
            lineNumber++;
            foreach (var p in Patterns)
            {
                if (p.Regex.IsMatch(line))
                {
                    findings.Add(new SecretFinding
                    {
                        RelativePath = relativePath,
                        Line = lineNumber,
                        Category = p.Category,
                        Recommendation = p.Recommendation
                    });
                    break;
                }
            }
        }

        return findings;
    }

    public static string ValidatePathInWorkspace(string targetPath, string workspaceRoot)
    {
        string canonicalWorkspace = Path.GetFullPath(workspaceRoot);
        string resolvedTarget = Path.GetFullPath(targetPath);

        string relative = Path.GetRelativePath(canonicalWorkspace, resolvedTarget);
        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
        {
            throw new InvalidOperationException(
                $"Security violation: Path '{targetPath}' resolves to '{resolvedTarget}', which is outside workspace '{canonicalWorkspace}'.");
        }

        string[] parts = relative.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
        if (Array.Exists(parts, p => p.Equals(".git", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Security violation: Reading from .git internal directories is prohibited.");
        }

        return resolvedTarget;
    }
}
