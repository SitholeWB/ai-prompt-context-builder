using System;
using System.Collections.Generic;
using System.Linq;
using AiPromptContextBuilder.Core.Graph;
using AiPromptContextBuilder.Core.Protocol;

namespace AiPromptContextBuilder.Core.Scoring;

public class ScoringWeights
{
    public int RootFile { get; set; } = 1000;
    public int FrameworkTemplate { get; set; } = 120;
    public int FrameworkCodeBehind { get; set; } = 115;
    public int ConstructorDependency { get; set; } = 110;
    public int BaseType { get; set; } = 105;
    public int ImplementedInterface { get; set; } = 100;
    public int DirectImport { get; set; } = 95;
    public int PublicParameterType { get; set; } = 90;
    public int PublicReturnType { get; set; } = 90;
    public int ComponentUsage { get; set; } = 90;
    public int FrameworkService { get; set; } = 90;
    public int PropertyType { get; set; } = 85;
    public int FieldType { get; set; } = 80;
    public int ObjectCreation { get; set; } = 80;
    public int RouteTarget { get; set; } = 80;
    public int HookUsage { get; set; } = 75;
    public int ContextOrStoreUsage { get; set; } = 75;
    public int InvokedContainingType { get; set; } = 70;
    public int ExtensionMethodContainer { get; set; } = 70;
    public int GenericArgument { get; set; } = 65;
    public int ExceptionType { get; set; } = 60;
    public int StyleCompanion { get; set; } = 55;
    public int AttributeAnnotationDecorator { get; set; } = 50;
    public int LocalVariableType { get; set; } = 40;
    public int TestFile { get; set; } = 30;
    public int StoryFile { get; set; } = 25;
    public int BinaryAsset { get; set; } = 0;
}

public static class ImportanceScorer
{
    public static Dictionary<string, int> CalculateScores(
        DependencyGraph graph,
        string rootNodeId,
        ScoringWeights? weights = null)
    {
        weights ??= new ScoringWeights();
        var scores = new Dictionary<string, int>(StringComparer.Ordinal);
        var rootNode = graph.GetNode(rootNodeId);
        if (rootNode == null) return scores;

        scores[rootNodeId] = weights.RootFile;
        rootNode.Importance = weights.RootFile;

        var bfsResults = graph.TraverseBfs(rootNodeId);
        var depthMap = bfsResults.ToDictionary(r => r.Node.Id, r => r.Depth, StringComparer.Ordinal);

        foreach (var item in bfsResults)
        {
            var node = item.Node;
            if (node.Id == rootNodeId) continue;

            var inEdges = graph.GetInEdges(node.Id);
            double baseScore = 0;
            var distinctRels = new HashSet<RelationshipType>();

            foreach (var edge in inEdges)
            {
                distinctRels.Add(edge.Relationship);
                int relWeight = GetRelationshipWeight(edge.Relationship, weights);
                double confMult = GetConfidenceMultiplier(edge.Confidence);
                baseScore += relWeight * confMult;
            }

            int depth = depthMap.GetValueOrDefault(node.Id, 99);
            double depthMultiplier = Math.Max(0.2, 1.0 - (depth - 1) * 0.15);

            bool isDirect = inEdges.Any(e => e.SourceNodeId == rootNodeId);
            int directBonus = isDirect ? 25 : 0;

            int refCountBonus = Math.Min(30, Math.Max(0, inEdges.Count - 1) * 10);
            int multiRelBonus = Math.Min(25, Math.Max(0, distinctRels.Count - 1) * 12);

            if (node.TestFile)
            {
                baseScore = weights.TestFile;
            }

            int finalScore = (int)Math.Round(baseScore * depthMultiplier + directBonus + refCountBonus + multiRelBonus);
            scores[node.Id] = finalScore;
            node.Importance = finalScore;
            node.Depth = depth;
        }

        return scores;
    }

    private static int GetRelationshipWeight(RelationshipType rel, ScoringWeights w) => rel switch
    {
        RelationshipType.Root => w.RootFile,
        RelationshipType.Template => w.FrameworkTemplate,
        RelationshipType.CodeBehind => w.FrameworkCodeBehind,
        RelationshipType.ConstructorDependency => w.ConstructorDependency,
        RelationshipType.BaseType => w.BaseType,
        RelationshipType.Interface or RelationshipType.Implementation => w.ImplementedInterface,
        RelationshipType.Import or RelationshipType.Export or RelationshipType.ReExport => w.DirectImport,
        RelationshipType.ParameterType => w.PublicParameterType,
        RelationshipType.ReturnType => w.PublicReturnType,
        RelationshipType.ComponentUsage => w.ComponentUsage,
        RelationshipType.Service => w.FrameworkService,
        RelationshipType.PropertyType => w.PropertyType,
        RelationshipType.FieldType => w.FieldType,
        RelationshipType.ObjectCreation => w.ObjectCreation,
        RelationshipType.Route or RelationshipType.LazyRoute => w.RouteTarget,
        RelationshipType.HookUsage => w.HookUsage,
        RelationshipType.ContextUsage or RelationshipType.StoreUsage => w.ContextOrStoreUsage,
        RelationshipType.MethodCall => w.InvokedContainingType,
        RelationshipType.ExtensionMethod => w.ExtensionMethodContainer,
        RelationshipType.GenericArgument or RelationshipType.GenericConstraint => w.GenericArgument,
        RelationshipType.ExceptionType => w.ExceptionType,
        RelationshipType.Style or RelationshipType.StyleReference or RelationshipType.CompanionFile => w.StyleCompanion,
        RelationshipType.Attribute or RelationshipType.Annotation or RelationshipType.Decorator => w.AttributeAnnotationDecorator,
        RelationshipType.LocalType => w.LocalVariableType,
        RelationshipType.AssetReference => w.BinaryAsset,
        _ => 50
    };

    private static double GetConfidenceMultiplier(Confidence c) => c switch
    {
        Confidence.Verified => 1.2,
        Confidence.High => 1.0,
        Confidence.Medium => 0.8,
        Confidence.Low => 0.6,
        Confidence.Unresolved => 0.3,
        _ => 1.0
    };
}
