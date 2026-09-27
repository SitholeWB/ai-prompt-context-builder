import {
  GraphNode,
  GraphEdge,
  DependencyGraphFragment,
  RelationshipType,
  Confidence,
  CapabilityLevel,
} from '../protocol/types.js';

export class DependencyGraph {
  private nodes = new Map<string, GraphNode>();
  private edges: GraphEdge[] = [];
  private outEdgeIndex = new Map<string, GraphEdge[]>();
  private inEdgeIndex = new Map<string, GraphEdge[]>();
  private fileToNodeIndex = new Map<string, string[]>();

  constructor() {}

  public addNode(node: GraphNode): void {
    const existing = this.nodes.get(node.id);
    if (existing) {
      // Merge node metadata & diagnostics without losing properties
      existing.diagnostics = [...existing.diagnostics, ...node.diagnostics];
      existing.metadata = { ...existing.metadata, ...node.metadata };
      if (!existing.content && node.content) {
        existing.content = node.content;
      }
      if (node.importance > existing.importance) {
        existing.importance = node.importance;
      }
      if (node.depth < existing.depth) {
        existing.depth = node.depth;
      }
      return;
    }

    this.nodes.set(node.id, { ...node, diagnostics: [...node.diagnostics], metadata: { ...node.metadata } });

    const fileList = this.fileToNodeIndex.get(node.relativePath) || [];
    fileList.push(node.id);
    this.fileToNodeIndex.set(node.relativePath, fileList);
  }

  public getNode(id: string): GraphNode | undefined {
    return this.nodes.get(id);
  }

  public hasNode(id: string): boolean {
    return this.nodes.has(id);
  }

  public getAllNodes(): GraphNode[] {
    return Array.from(this.nodes.values());
  }

  public addEdge(edge: GraphEdge): void {
    // Preserve all distinct relationship types without duplicating identical edge
    const existing = this.outEdgeIndex.get(edge.sourceNodeId) || [];
    const isDuplicate = existing.some(
      (e) =>
        e.targetNodeId === edge.targetNodeId &&
        e.relationship === edge.relationship
    );

    if (isDuplicate) {
      return;
    }

    this.edges.push(edge);

    // Update out index
    existing.push(edge);
    this.outEdgeIndex.set(edge.sourceNodeId, existing);

    // Update in index
    const inExisting = this.inEdgeIndex.get(edge.targetNodeId) || [];
    inExisting.push(edge);
    this.inEdgeIndex.set(edge.targetNodeId, inExisting);
  }

  public getEdges(): GraphEdge[] {
    return [...this.edges];
  }

  public getOutEdges(sourceId: string): GraphEdge[] {
    return this.outEdgeIndex.get(sourceId) || [];
  }

  public getInEdges(targetId: string): GraphEdge[] {
    return this.inEdgeIndex.get(targetId) || [];
  }

  public getNodesByFile(relativePath: string): GraphNode[] {
    const ids = this.fileToNodeIndex.get(relativePath) || [];
    return ids.map((id) => this.nodes.get(id)!).filter(Boolean);
  }

  public getAllFiles(): string[] {
    return Array.from(this.fileToNodeIndex.keys());
  }

  public mergeFragment(fragment: DependencyGraphFragment): void {
    for (const node of fragment.nodes) {
      this.addNode(node);
    }
    for (const edge of fragment.edges) {
      this.addEdge(edge);
    }
  }

  public clone(): DependencyGraph {
    const copy = new DependencyGraph();
    for (const node of this.getAllNodes()) {
      copy.addNode({ ...node });
    }
    for (const edge of this.edges) {
      copy.addEdge({ ...edge });
    }
    return copy;
  }

  /**
   * Breadth-First Search traversal starting from root node.
   * Prevents circular traversal and assigns deterministic depths.
   */
  public traverseBfs(rootId: string, maxDepth: number | null = null): {
    node: GraphNode;
    depth: number;
    parentEdge?: GraphEdge;
  }[] {
    const result: { node: GraphNode; depth: number; parentEdge?: GraphEdge }[] = [];
    const rootNode = this.nodes.get(rootId);
    if (!rootNode) return result;

    const visitedNodes = new Set<string>();
    const queue: { nodeId: string; depth: number; parentEdge?: GraphEdge }[] = [
      { nodeId: rootId, depth: 0 },
    ];
    visitedNodes.add(rootId);

    while (queue.length > 0) {
      const current = queue.shift()!;
      const node = this.nodes.get(current.nodeId);
      if (!node) continue;

      node.depth = current.depth;
      result.push({ node, depth: current.depth, parentEdge: current.parentEdge });

      if (maxDepth !== null && current.depth >= maxDepth) {
        continue;
      }

      // Deterministic ordering of outgoing edges: target relativePath, then relationship
      const outEdges = [...this.getOutEdges(current.nodeId)].sort((a, b) => {
        const nodeA = this.nodes.get(a.targetNodeId);
        const nodeB = this.nodes.get(b.targetNodeId);
        const pathA = nodeA ? nodeA.relativePath : a.targetNodeId;
        const pathB = nodeB ? nodeB.relativePath : b.targetNodeId;
        if (pathA !== pathB) return pathA.localeCompare(pathB);
        return a.relationship.localeCompare(b.relationship);
      });

      for (const edge of outEdges) {
        if (!visitedNodes.has(edge.targetNodeId)) {
          visitedNodes.add(edge.targetNodeId);
          queue.push({
            nodeId: edge.targetNodeId,
            depth: current.depth + 1,
            parentEdge: edge,
          });
        }
      }
    }

    return result;
  }

  /**
   * Formats ASCII dependency tree using ├── and └── branch symbols.
   * Handles cycles gracefully by annotating (cycle/already shown).
   */
  public renderDependencyTree(rootNodeId: string): string {
    const rootNode = this.nodes.get(rootNodeId);
    if (!rootNode) return '(Empty dependency tree)';

    const lines: string[] = [];
    lines.push(rootNode.relativePath || rootNode.displayName || rootNode.id);

    const visitedInPath = new Set<string>();
    const globalVisited = new Set<string>();
    visitedInPath.add(rootNodeId);
    globalVisited.add(rootNodeId);

    const renderChildren = (nodeId: string, prefix: string) => {
      const edges = [...this.getOutEdges(nodeId)].sort((a, b) => {
        const targetA = this.nodes.get(a.targetNodeId);
        const targetB = this.nodes.get(b.targetNodeId);
        const pathA = targetA ? targetA.relativePath : a.targetNodeId;
        const pathB = targetB ? targetB.relativePath : b.targetNodeId;
        if (pathA !== pathB) return pathA.localeCompare(pathB);
        return a.relationship.localeCompare(b.relationship);
      });

      for (let i = 0; i < edges.length; i++) {
        const edge = edges[i];
        const isLast = i === edges.length - 1;
        const branch = isLast ? '└── ' : '├── ';
        const childPrefix = prefix + (isLast ? '    ' : '│   ');
        const targetNode = this.nodes.get(edge.targetNodeId);
        const targetLabel = targetNode ? targetNode.relativePath : edge.targetNodeId;

        const relLabel = edge.relationship ? edge.relationship.toLowerCase() : 'reference';
        const confLabel = edge.confidence.toLowerCase();
        const annotation = `[${relLabel}, ${confLabel}]`;

        if (visitedInPath.has(edge.targetNodeId)) {
          lines.push(`${prefix}${branch}${targetLabel} ${annotation} (circular)`);
        } else if (globalVisited.has(edge.targetNodeId)) {
          lines.push(`${prefix}${branch}${targetLabel} ${annotation} (already shown)`);
        } else {
          lines.push(`${prefix}${branch}${targetLabel} ${annotation}`);
          globalVisited.add(edge.targetNodeId);
          visitedInPath.add(edge.targetNodeId);
          renderChildren(edge.targetNodeId, childPrefix);
          visitedInPath.delete(edge.targetNodeId);
        }
      }
    };

    renderChildren(rootNodeId, '');
    return lines.join('\n');
  }
}
