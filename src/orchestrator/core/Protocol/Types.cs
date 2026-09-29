using System;
using System.Collections.Generic;

namespace AiPromptContextBuilder.Core.Protocol;

public enum CapabilityLevel
{
    Semantic,
    SyntaxAware,
    ImportGraph,
    CompanionFile,
    ReferenceOnly
}

public enum Confidence
{
    Verified,
    High,
    Medium,
    Low,
    Unresolved
}

public enum NodeKind
{
    File,
    Type,
    Class,
    Interface,
    Struct,
    Record,
    Enum,
    Function,
    Method,
    Module,
    Package,
    Component,
    Template,
    Style,
    Route,
    Service,
    Hook,
    Store,
    Configuration,
    Asset,
    Unknown
}

public enum RelationshipType
{
    Root,
    Import,
    Export,
    ReExport,
    DynamicImport,
    ProjectReference,
    PackageReference,
    BaseType,
    Interface,
    Implementation,
    ConstructorDependency,
    ParameterType,
    ReturnType,
    PropertyType,
    FieldType,
    EventType,
    GenericArgument,
    GenericConstraint,
    ObjectCreation,
    MethodCall,
    ExtensionMethod,
    Attribute,
    Decorator,
    Annotation,
    ExceptionType,
    LocalType,
    ConversionType,
    Template,
    Style,
    Route,
    LazyRoute,
    ComponentUsage,
    HookUsage,
    ContextUsage,
    StoreUsage,
    CodeBehind,
    PartialDeclaration,
    CompanionFile,
    ScriptReference,
    StyleReference,
    AssetReference,
    ConfigurationReference,
    FrameworkConvention,
    Service,
    Unknown
}

public enum DiagnosticSeverity
{
    Info,
    Warning,
    Error,
    Fatal
}

public enum CommentMode
{
    Preserve,
    Remove,
    Auto
}

public enum LanguageName
{
    Auto,
    CSharp,
    VisualBasic,
    TypeScript,
    JavaScript,
    JSX,
    TSX,
    HTML,
    CSS,
    SCSS,
    Sass,
    Java,
    Python,
    Go,
    Razor,
    Vue,
    Rust,
    Kotlin,
    Php,
    Dart,
    Cpp,
    C
}

public enum FrameworkName
{
    Auto,
    None,
    AspNetCore,
    RazorPages,
    Blazor,
    Node,
    React,
    Angular,
    Vue
}

public enum RequestedAnalysisLevel
{
    Best,
    Semantic,
    SyntaxAware,
    ImportGraph
}

public enum ExclusionReason
{
    TokenBudget,
    MaxDepth,
    TestFileExcluded,
    GeneratedFileExcluded,
    AssetExcluded,
    ConfigurationFileExcluded,
    ExcludedProject,
    ExcludedPackage,
    ExcludedNamespace,
    ExcludeGlob,
    NoSourceDeclaration,
    ExternalDependency,
    UnsupportedDocument,
    DuplicatePhysicalFile,
    UnresolvedDependency,
    OutsideWorkspace,
    SecurityRestriction
}

public enum ErrorCode
{
    FileNotFound,
    WorkspaceNotFound,
    ProjectNotFound,
    UnsupportedLanguage,
    UnsupportedFramework,
    AdapterNotInstalled,
    AdapterVersionMismatch,
    WorkerStartFailed,
    WorkerProtocolError,
    DocumentNotInWorkspace,
    NoRootSymbol,
    MultipleRootSymbols,
    RootSymbolNotFound,
    WorkspaceLoadFailed,
    RestoreRequired,
    BuildToolNotFound,
    InvalidConfiguration,
    GenerationCancelled,
    OutputWriteFailed,
    ClipboardFailed,
    OutsideWorkspace,
    UntrustedWorkspace,
    TokenBudgetTooSmall,
    CommentRemovalUnsupported,
    UnexpectedError
}

public class Diagnostic
{
    public string Code { get; set; } = string.Empty;
    public DiagnosticSeverity Severity { get; set; } = DiagnosticSeverity.Info;
    public string Message { get; set; } = string.Empty;
    public string? RelativePath { get; set; }
    public int? Line { get; set; }
    public int? Column { get; set; }
    public string Adapter { get; set; } = string.Empty;
    public bool Recoverable { get; set; } = true;
    public bool AffectsCompleteness { get; set; } = false;
}

public class GraphNode
{
    public string Id { get; set; } = string.Empty;
    public NodeKind Kind { get; set; } = NodeKind.File;
    public string Language { get; set; } = string.Empty;
    public string? Framework { get; set; }
    public CapabilityLevel AnalysisLevel { get; set; } = CapabilityLevel.Semantic;
    public string DisplayName { get; set; } = string.Empty;
    public string QualifiedName { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public string? ProjectOrPackage { get; set; }
    public bool SourceAvailable { get; set; } = true;
    public bool Generated { get; set; } = false;
    public bool TestFile { get; set; } = false;
    public int Depth { get; set; } = 0;
    public int Importance { get; set; } = 0;
    public List<Diagnostic> Diagnostics { get; set; } = new();
    public Dictionary<string, object> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? Content { get; set; }
}

public class GraphEdge
{
    public string SourceNodeId { get; set; } = string.Empty;
    public string TargetNodeId { get; set; } = string.Empty;
    public RelationshipType Relationship { get; set; } = RelationshipType.Import;
    public Confidence Confidence { get; set; } = Confidence.Verified;
    public CapabilityLevel AnalysisLevel { get; set; } = CapabilityLevel.Semantic;
    public int? SourceLine { get; set; }
    public int? SourceColumn { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public class DependencyGraphFragment
{
    public List<GraphNode> Nodes { get; set; } = new();
    public List<GraphEdge> Edges { get; set; } = new();
    public List<Diagnostic> Diagnostics { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public List<string> DynamicConstructs { get; set; } = new();
}

public class SecretFinding
{
    public string RelativePath { get; set; } = string.Empty;
    public int Line { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Recommendation { get; set; } = string.Empty;
}

public class UserConfiguration
{
    public string RootPath { get; set; } = string.Empty;
    public string? WorkspacePath { get; set; }
    public string? ProjectPath { get; set; }
    public string? RootSymbol { get; set; }
    public string? OutputPath { get; set; }

    public LanguageName Language { get; set; } = LanguageName.Auto;
    public FrameworkName Framework { get; set; } = FrameworkName.Auto;
    public RequestedAnalysisLevel AnalysisLevel { get; set; } = RequestedAnalysisLevel.Best;

    public int? MaxTokens { get; set; }
    public int? MaxDepth { get; set; }
    public CommentMode CommentMode { get; set; } = CommentMode.Preserve;

    public bool IncludeTests { get; set; } = false;
    public bool IncludeGeneratedFiles { get; set; } = false;
    public bool IncludeAttributes { get; set; } = true;
    public bool IncludeImplementations { get; set; } = true;
    public bool IncludeEventSubscribers { get; set; } = true;
    public bool IncludeCompanionFiles { get; set; } = true;
    public bool IncludeAssets { get; set; } = false;
    public bool IncludeConfigurationFiles { get; set; } = false;
    public bool IncludeMetadataDependenciesInTree { get; set; } = true;
    public bool IncludeDependencyTree { get; set; } = true;
    public bool IncludeExcludedFileList { get; set; } = true;
    public bool IncludeGenerationMetadata { get; set; } = true;

    public bool CopyToClipboard { get; set; } = false;
    public bool SaveToFile { get; set; } = true;
    public bool OpenAfterGeneration { get; set; } = false;

    public string? Task { get; set; }

    public List<string> IncludeGlobs { get; set; } = new();
    public List<string> ExcludeGlobs { get; set; } = new();
    public List<string> ExcludedProjects { get; set; } = new();
    public List<string> ExcludedPackages { get; set; } = new();
    public List<string> ExcludedNamespaces { get; set; } = new();
    public List<string> ExcludedDirectories { get; set; } = new();

    public string Tokenizer { get; set; } = "approximate";
    public bool Verbose { get; set; } = false;
    public bool OmitTimestamp { get; set; } = false;
}

public class WorkerProtocolRequest
{
    public string ProtocolVersion { get; set; } = "1.0";
    public string RequestId { get; set; } = Guid.NewGuid().ToString();
    public string Operation { get; set; } = "analyse"; // analyse, detect, removeComments, ping, capabilities
    public string? RootPath { get; set; }
    public string? FilePath { get; set; }
    public string WorkspacePath { get; set; } = string.Empty;
    public string? Language { get; set; }
    public string? Framework { get; set; }
    public string? Code { get; set; }
    public Dictionary<string, object> Options { get; set; } = new();
}

public class WorkerAdapterDescriptor
{
    public string Id { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Capability { get; set; } = "Semantic";
}

public class DetectedFramework
{
    public string Id { get; set; } = string.Empty;
    public string Confidence { get; set; } = "Verified";
}

public class WorkerProtocolResponse
{
    public string ProtocolVersion { get; set; } = "1.0";
    public string RequestId { get; set; } = string.Empty;
    public bool Success { get; set; }
    public WorkerAdapterDescriptor? Adapter { get; set; }
    public List<DetectedFramework>? Frameworks { get; set; }
    public DependencyGraphFragment? Graph { get; set; }
    public string? Code { get; set; }
    public List<Diagnostic>? Diagnostics { get; set; }
    public List<string>? Warnings { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
}
