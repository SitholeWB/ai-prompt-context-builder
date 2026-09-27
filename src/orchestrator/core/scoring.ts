import { DependencyGraph } from './graph.js';
import { GraphNode, GraphEdge, RelationshipType, Confidence } from '../protocol/types.js';

export interface ScoringWeights {
  rootFile: number;
  frameworkTemplate: number;
  frameworkCodeBehind: number;
  constructorDependency: number;
  baseType: number;
  implementedInterface: number;
  directImport: number;
  publicParameterType: number;
  publicReturnType: number;
  componentUsage: number;
  frameworkService: number;
  propertyType: number;
  fieldType: number;
  objectCreation: number;
  routeTarget: number;
  hookUsage: number;
  contextOrStoreUsage: number;
  invokedContainingType: number;
  extensionMethodContainer: number;
  genericArgument: number;
  exceptionType: number;
  styleCompanion: number;
  attributeAnnotationDecorator: number;
  localVariableType: number;
  testFile: number;
  storyFile: number;
  binaryAsset: number;
}

export const DEFAULT_SCORING_WEIGHTS: ScoringWeights = {
  rootFile: 1000,
  frameworkTemplate: 120,
  frameworkCodeBehind: 115,
  constructorDependency: 110,
  baseType: 105,
  implementedInterface: 100,
  directImport: 95,
  publicParameterType: 90,
  publicReturnType: 90,
  componentUsage: 90,
  frameworkService: 90,
  propertyType: 85,
  fieldType: 80,
  objectCreation: 80,
  routeTarget: 80,
  hookUsage: 75,
  contextOrStoreUsage: 75,
  invokedContainingType: 70,
  extensionMethodContainer: 70,
  genericArgument: 65,
  exceptionType: 60,
  styleCompanion: 55,
  attributeAnnotationDecorator: 50,
  localVariableType: 40,
  testFile: 30,
  storyFile: 25,
  binaryAsset: 0,
};

function getRelationshipWeight(rel: RelationshipType, weights: ScoringWeights): number {
  switch (rel) {
    case 'Root':
      return weights.rootFile;
    case 'Template':
      return weights.frameworkTemplate;
    case 'CodeBehind':
      return weights.frameworkCodeBehind;
    case 'ConstructorDependency':
      return weights.constructorDependency;
    case 'BaseType':
      return weights.baseType;
    case 'Interface':
    case 'Implementation':
      return weights.implementedInterface;
    case 'Import':
    case 'Export':
    case 'ReExport':
      return weights.directImport;
    case 'ParameterType':
      return weights.publicParameterType;
    case 'ReturnType':
      return weights.publicReturnType;
    case 'ComponentUsage':
      return weights.componentUsage;
    case 'Service':
      return weights.frameworkService;
    case 'PropertyType':
      return weights.propertyType;
    case 'FieldType':
      return weights.fieldType;
    case 'ObjectCreation':
      return weights.objectCreation;
    case 'Route':
    case 'LazyRoute':
      return weights.routeTarget;
    case 'HookUsage':
      return weights.hookUsage;
    case 'ContextUsage':
    case 'StoreUsage':
      return weights.contextOrStoreUsage;
    case 'MethodCall':
      return weights.invokedContainingType;
    case 'ExtensionMethod':
      return weights.extensionMethodContainer;
    case 'GenericArgument':
    case 'GenericConstraint':
      return weights.genericArgument;
    case 'ExceptionType':
      return weights.exceptionType;
    case 'Style':
    case 'StyleReference':
    case 'CompanionFile':
      return weights.styleCompanion;
    case 'Attribute':
    case 'Annotation':
    case 'Decorator':
      return weights.attributeAnnotationDecorator;
    case 'LocalType':
      return weights.localVariableType;
    case 'AssetReference':
      return weights.binaryAsset;
    default:
      return 50;
  }
}

function getConfidenceMultiplier(confidence: Confidence): number {
  switch (confidence) {
    case 'Verified':
      return 1.2;
    case 'High':
      return 1.0;
    case 'Medium':
      return 0.8;
    case 'Low':
      return 0.6;
    case 'Unresolved':
      return 0.3;
    default:
      return 1.0;
  }
}

export function calculateImportanceScores(
  graph: DependencyGraph,
  rootNodeId: string,
  weights: ScoringWeights = DEFAULT_SCORING_WEIGHTS
): Map<string, number> {
  const scores = new Map<string, number>();
  const rootNode = graph.getNode(rootNodeId);
  if (!rootNode) return scores;

  // Root node always gets base root score
  scores.set(rootNodeId, weights.rootFile);
  rootNode.importance = weights.rootFile;

  // Traverse all nodes using BFS to know their depth from root
  const bfsResults = graph.traverseBfs(rootNodeId);
  const depthMap = new Map<string, number>();
  for (const item of bfsResults) {
    depthMap.set(item.node.id, item.depth);
  }

  // Calculate scores for all other nodes
  for (const item of bfsResults) {
    const node = item.node;
    if (node.id === rootNodeId) continue;

    const inEdges = graph.getInEdges(node.id);
    let baseScore = 0;
    const distinctRelationships = new Set<RelationshipType>();
    let maxConfidence: Confidence = 'Low';

    for (const edge of inEdges) {
      distinctRelationships.add(edge.relationship);
      const relWeight = getRelationshipWeight(edge.relationship, weights);
      const confMult = getConfidenceMultiplier(edge.confidence);
      baseScore += relWeight * confMult;

      if (edge.confidence === 'Verified') maxConfidence = 'Verified';
      else if (edge.confidence === 'High' && maxConfidence !== 'Verified') maxConfidence = 'High';
      else if (edge.confidence === 'Medium' && maxConfidence === 'Low') maxConfidence = 'Medium';
    }

    // Depth penalty factor: shorter depth scores higher
    const depth = depthMap.get(node.id) ?? 99;
    const depthMultiplier = Math.max(0.2, 1.0 - (depth - 1) * 0.15);

    // Direct dependency of root gets bonus
    const isDirect = inEdges.some((e) => e.sourceNodeId === rootNodeId);
    const directBonus = isDirect ? 25 : 0;

    // Multi-reference bonus: multiple references and diverse relationships
    const refCountBonus = Math.min(30, (inEdges.length - 1) * 10);
    const multiRelBonus = Math.min(25, (distinctRelationships.size - 1) * 12);

    // Test file or asset adjustments
    if (node.testFile) {
      baseScore = weights.testFile;
    }

    const finalScore = Math.round(
      baseScore * depthMultiplier + directBonus + refCountBonus + multiRelBonus
    );

    scores.set(node.id, finalScore);
    node.importance = finalScore;
    node.depth = depth;
  }

  return scores;
}
