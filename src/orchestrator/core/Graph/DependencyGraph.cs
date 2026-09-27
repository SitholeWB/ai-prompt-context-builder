using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AiContextBuilder.Core.Protocol;

namespace AiContextBuilder.Core.Graph;

public class BfsTraversalItem
{
    public required GraphNode Node { get; set; }
    public int Depth { get; set; }
    public GraphEdge? ParentEdge { get; set; }
}

public class DependencyGraph
{
    private readonly Dictionary<string, GraphNode> _nodes = new(StringComparer.Ordinal);
    private readonly List<GraphEdge> _edges = new();
    private readonly Dictionary<string, List<GraphEdge>> _outEdgeIndex = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<GraphEdge>> _inEdgeIndex = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> _fileToNodeIndex = new(StringComparer.OrdinalIgnoreCase);

    public void AddNode(GraphNode node)
    {
        if (_nodes.TryGetValue(node.Id, out var existing))
        {
            existing.Diagnostics.AddRange(node.Diagnostics);
            foreach (var kvp in node.Metadata)
            {
                existing.Metadata[kvp.Key] = kvp.Value;
            }
            if (string.IsNullOrEmpty(existing.Content) && !string.IsNullOrEmpty(node.Content))
            {
                existing.Content = node.Content;
            }
            if (node.Importance > existing.Importance)
            {
                existing.Importance = node.Importance;
            }
            if (node.Depth < existing.Depth)
            {
                existing.Depth = node.Depth;
            }
            return;
        }

        var clone = new GraphNode
        {
            Id = node.Id,
            Kind = node.Kind,
            Language = node.Language,
            Framework = node.Framework,
            AnalysisLevel = node.AnalysisLevel,
            DisplayName = node.DisplayName,
            QualifiedName = node.QualifiedName,
            RelativePath = node.RelativePath,
            ProjectOrPackage = node.ProjectOrPackage,
            SourceAvailable = node.SourceAvailable,
            Generated = node.Generated,
            TestFile = node.TestFile,
            Depth = node.Depth,
            Importance = node.Importance,
            Diagnostics = new List<Diagnostic>(node.Diagnostics),
            Metadata = new Dictionary<string, object>(node.Metadata, StringComparer.OrdinalIgnoreCase),
            Content = node.Content
        };

        _nodes[clone.Id] = clone;

        if (!_fileToNodeIndex.TryGetValue(clone.RelativePath, out var fileNodes))
        {
            fileNodes = new List<string>();
            _fileToNodeIndex[clone.RelativePath] = fileNodes;
        }
        fileNodes.Add(clone.Id);
    }

    public GraphNode? GetNode(string id) => _nodes.GetValueOrDefault(id);

    public bool HasNode(string id) => _nodes.ContainsKey(id);

    public IReadOnlyCollection<GraphNode> GetAllNodes() => _nodes.Values;

    public void AddEdge(GraphEdge edge)
    {
        if (!_outEdgeIndex.TryGetValue(edge.SourceNodeId, out var outList))
        {
            outList = new List<GraphEdge>();
            _outEdgeIndex[edge.SourceNodeId] = outList;
        }

        // Preserve all distinct relationship types without duplicating identical edge
        bool alreadyExists = outList.Any(e =>
            e.TargetNodeId == edge.TargetNodeId &&
            e.Relationship == edge.Relationship);

        if (alreadyExists) return;

        _edges.Add(edge);
        outList.Add(edge);

        if (!_inEdgeIndex.TryGetValue(edge.TargetNodeId, out var inList))
        {
            inList = new List<GraphEdge>();
            _inEdgeIndex[edge.TargetNodeId] = inList;
        }
        inList.Add(edge);
    }

    public IReadOnlyList<GraphEdge> GetEdges() => _edges;

    public IReadOnlyList<GraphEdge> GetOutEdges(string sourceId) =>
        _outEdgeIndex.TryGetValue(sourceId, out var list) ? list : Array.Empty<GraphEdge>();

    public IReadOnlyList<GraphEdge> GetInEdges(string targetId) =>
        _inEdgeIndex.TryGetValue(targetId, out var list) ? list : Array.Empty<GraphEdge>();

    public IReadOnlyList<GraphNode> GetNodesByFile(string relativePath)
    {
        if (_fileToNodeIndex.TryGetValue(relativePath, out var ids))
        {
            return ids.Select(id => _nodes.GetValueOrDefault(id)).Where(n => n != null).ToList()!;
        }
        return Array.Empty<GraphNode>();
    }

    public IReadOnlyCollection<string> GetAllFiles() => _fileToNodeIndex.Keys;

    public void MergeFragment(DependencyGraphFragment fragment)
    {
        foreach (var node in fragment.Nodes)
        {
            AddNode(node);
        }
        foreach (var edge in fragment.Edges)
        {
            AddEdge(edge);
        }
    }

    public DependencyGraph Clone()
    {
        var copy = new DependencyGraph();
        foreach (var node in _nodes.Values)
        {
            copy.AddNode(node);
        }
        foreach (var edge in _edges)
        {
            copy.AddEdge(edge);
        }
        return copy;
    }

    public List<BfsTraversalItem> TraverseBfs(string rootId, int? maxDepth = null)
    {
        var result = new List<BfsTraversalItem>();
        if (!_nodes.TryGetValue(rootId, out var rootNode))
            return result;

        var visited = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<(string nodeId, int depth, GraphEdge? parentEdge)>();

        queue.Enqueue((rootId, 0, null));
        visited.Add(rootId);

        while (queue.Count > 0)
        {
            var (currentId, depth, parentEdge) = queue.Dequeue();
            if (!_nodes.TryGetValue(currentId, out var node))
                continue;

            node.Depth = depth;
            result.Add(new BfsTraversalItem { Node = node, Depth = depth, ParentEdge = parentEdge });

            if (maxDepth.HasValue && depth >= maxDepth.Value)
                continue;

            var outEdges = GetOutEdges(currentId)
                .OrderBy(e =>
                {
                    var target = GetNode(e.TargetNodeId);
                    return target != null ? target.RelativePath : e.TargetNodeId;
                }, StringComparer.OrdinalIgnoreCase)
                .ThenBy(e => e.Relationship.ToString())
                .ToList();

            foreach (var edge in outEdges)
            {
                if (visited.Add(edge.TargetNodeId))
                {
                    queue.Enqueue((edge.TargetNodeId, depth + 1, edge));
                }
            }
        }

        return result;
    }

    public string RenderDependencyTree(string rootNodeId)
    {
        if (!_nodes.TryGetValue(rootNodeId, out var rootNode))
            return "(Empty dependency tree)";

        var sb = new StringBuilder();
        sb.AppendLine(string.IsNullOrEmpty(rootNode.RelativePath) ? rootNode.DisplayName : rootNode.RelativePath);

        var visitedInPath = new HashSet<string>(StringComparer.Ordinal);
        var globalVisited = new HashSet<string>(StringComparer.Ordinal);
        visitedInPath.Add(rootNodeId);
        globalVisited.Add(rootNodeId);

        void RenderChildren(string nodeId, string prefix)
        {
            var edges = GetOutEdges(nodeId)
                .OrderBy(e =>
                {
                    var target = GetNode(e.TargetNodeId);
                    return target != null ? target.RelativePath : e.TargetNodeId;
                }, StringComparer.OrdinalIgnoreCase)
                .ThenBy(e => e.Relationship.ToString())
                .ToList();

            for (int i = 0; i < edges.Count; i++)
            {
                var edge = edges[i];
                bool isLast = i == edges.Count - 1;
                string branch = isLast ? "└── " : "├── ";
                string childPrefix = prefix + (isLast ? "    " : "│   ");

                var targetNode = GetNode(edge.TargetNodeId);
                string targetLabel = targetNode != null ? targetNode.RelativePath : edge.TargetNodeId;
                string relLabel = edge.Relationship.ToString().ToLowerInvariant();
                string confLabel = edge.Confidence.ToString().ToLowerInvariant();
                string annotation = $"[{relLabel}, {confLabel}]";

                if (visitedInPath.Contains(edge.TargetNodeId))
                {
                    sb.AppendLine($"{prefix}{branch}{targetLabel} {annotation} (circular)");
                }
                else if (globalVisited.Contains(edge.TargetNodeId))
                {
                    sb.AppendLine($"{prefix}{branch}{targetLabel} {annotation} (already shown)");
                }
                else
                {
                    sb.AppendLine($"{prefix}{branch}{targetLabel} {annotation}");
                    globalVisited.Add(edge.TargetNodeId);
                    visitedInPath.Add(edge.TargetNodeId);
                    RenderChildren(edge.TargetNodeId, childPrefix);
                    visitedInPath.Remove(edge.TargetNodeId);
                }
            }
        }

        RenderChildren(rootNodeId, "");
        return sb.ToString().TrimEnd();
    }
}
