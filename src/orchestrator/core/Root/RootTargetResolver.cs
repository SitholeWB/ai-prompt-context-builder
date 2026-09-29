using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AiPromptContextBuilder.Core.Protocol;

namespace AiPromptContextBuilder.Core.Root;

public class RootSelectionResult
{
    public string RootNodeId { get; set; } = string.Empty;
    public string? RootSymbol { get; set; }
    public bool IsSingleCandidate { get; set; }
    public List<string> Candidates { get; set; } = new();
}

public static class RootTargetResolver
{
    private static readonly HashSet<string> DirectFileRootExts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".html", ".htm", ".css", ".scss", ".sass", ".vue", ".razor", ".cshtml"
    };

    public static RootSelectionResult Resolve(
        List<GraphNode> nodesInRootFile,
        string rootFilePath,
        string? requestedSymbol = null)
    {
        string ext = Path.GetExtension(rootFilePath);

        if (DirectFileRootExts.Contains(ext))
        {
            var fileNode = nodesInRootFile.FirstOrDefault(n => n.Kind is NodeKind.File or NodeKind.Component or NodeKind.Template)
                ?? nodesInRootFile.FirstOrDefault();

            if (fileNode != null)
            {
                return new RootSelectionResult
                {
                    RootNodeId = fileNode.Id,
                    IsSingleCandidate = true,
                    Candidates = new List<string> { fileNode.DisplayName }
                };
            }
        }

        var declarationNodes = nodesInRootFile.Where(n =>
            n.Kind is NodeKind.Class or NodeKind.Interface or NodeKind.Record or
                      NodeKind.Struct or NodeKind.Enum or NodeKind.Function or
                      NodeKind.Component or NodeKind.Service).ToList();

        if (!string.IsNullOrWhiteSpace(requestedSymbol))
        {
            string target = requestedSymbol.Trim();
            var matched = declarationNodes.FirstOrDefault(n =>
                string.Equals(n.QualifiedName, target, StringComparison.Ordinal) ||
                string.Equals(n.DisplayName, target, StringComparison.Ordinal));

            if (matched == null)
            {
                var candidates = declarationNodes.Select(n => !string.IsNullOrEmpty(n.QualifiedName) ? n.QualifiedName : n.DisplayName).ToList();
                throw new InvalidOperationException(
                    $"Root symbol '{requestedSymbol}' was not found in file '{rootFilePath}'. Available candidates: {string.Join(", ", candidates)}");
            }

            return new RootSelectionResult
            {
                RootNodeId = matched.Id,
                RootSymbol = !string.IsNullOrEmpty(matched.QualifiedName) ? matched.QualifiedName : matched.DisplayName,
                IsSingleCandidate = true,
                Candidates = new List<string> { matched.QualifiedName }
            };
        }

        if (declarationNodes.Count == 0)
        {
            var fileNode = nodesInRootFile.FirstOrDefault(n => n.Kind == NodeKind.File)
                ?? nodesInRootFile.FirstOrDefault()
                ?? throw new InvalidOperationException($"No root nodes found for file '{rootFilePath}'.");

            return new RootSelectionResult
            {
                RootNodeId = fileNode.Id,
                IsSingleCandidate = true,
                Candidates = new List<string> { fileNode.DisplayName }
            };
        }

        if (declarationNodes.Count == 1)
        {
            var node = declarationNodes[0];
            return new RootSelectionResult
            {
                RootNodeId = node.Id,
                RootSymbol = !string.IsNullOrEmpty(node.QualifiedName) ? node.QualifiedName : node.DisplayName,
                IsSingleCandidate = true,
                Candidates = new List<string> { node.QualifiedName }
            };
        }

        // Multiple candidates: check if one matches the filename (supporting snake_case and kebab-case matching)
        string fileNameWithoutExt = Path.GetFileNameWithoutExtension(rootFilePath);
        string normalizedFileName = fileNameWithoutExt.Replace("_", "").Replace("-", "");
        var matchingName = declarationNodes.FirstOrDefault(n =>
            string.Equals(n.DisplayName, fileNameWithoutExt, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(n.DisplayName.Replace("_", "").Replace("-", ""), normalizedFileName, StringComparison.OrdinalIgnoreCase));
        if (matchingName != null)
        {
            return new RootSelectionResult
            {
                RootNodeId = matchingName.Id,
                RootSymbol = !string.IsNullOrEmpty(matchingName.QualifiedName) ? matchingName.QualifiedName : matchingName.DisplayName,
                IsSingleCandidate = true,
                Candidates = new List<string> { matchingName.QualifiedName }
            };
        }

        var allCandidates = declarationNodes.Select(n => !string.IsNullOrEmpty(n.QualifiedName) ? n.QualifiedName : n.DisplayName).ToList();
        var fallbackFile = nodesInRootFile.FirstOrDefault(n => n.Kind == NodeKind.File);

        return new RootSelectionResult
        {
            RootNodeId = fallbackFile?.Id ?? declarationNodes[0].Id,
            RootSymbol = null,
            IsSingleCandidate = false,
            Candidates = allCandidates
        };
    }
}
