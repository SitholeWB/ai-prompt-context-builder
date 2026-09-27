/**
 * AI Context Builder - Protocol & Data Contract Types
 * Version 1.0
 */

export type CapabilityLevel =
  | 'Semantic'
  | 'SyntaxAware'
  | 'ImportGraph'
  | 'CompanionFile'
  | 'ReferenceOnly';

export type Confidence =
  | 'Verified'
  | 'High'
  | 'Medium'
  | 'Low'
  | 'Unresolved';

export type NodeKind =
  | 'File'
  | 'Type'
  | 'Class'
  | 'Interface'
  | 'Struct'
  | 'Record'
  | 'Enum'
  | 'Function'
  | 'Method'
  | 'Module'
  | 'Package'
  | 'Component'
  | 'Template'
  | 'Style'
  | 'Route'
  | 'Service'
  | 'Hook'
  | 'Store'
  | 'Configuration'
  | 'Asset'
  | 'Unknown';

export type RelationshipType =
  | 'Root'
  | 'Import'
  | 'Export'
  | 'ReExport'
  | 'DynamicImport'
  | 'ProjectReference'
  | 'PackageReference'
  | 'BaseType'
  | 'Interface'
  | 'Implementation'
  | 'ConstructorDependency'
  | 'ParameterType'
  | 'ReturnType'
  | 'PropertyType'
  | 'FieldType'
  | 'EventType'
  | 'GenericArgument'
  | 'GenericConstraint'
  | 'ObjectCreation'
  | 'MethodCall'
  | 'ExtensionMethod'
  | 'Attribute'
  | 'Decorator'
  | 'Annotation'
  | 'ExceptionType'
  | 'LocalType'
  | 'ConversionType'
  | 'Template'
  | 'Style'
  | 'Route'
  | 'LazyRoute'
  | 'ComponentUsage'
  | 'HookUsage'
  | 'ContextUsage'
  | 'StoreUsage'
  | 'CodeBehind'
  | 'PartialDeclaration'
  | 'CompanionFile'
  | 'ScriptReference'
  | 'StyleReference'
  | 'AssetReference'
  | 'ConfigurationReference'
  | 'FrameworkConvention'
  | 'Unknown';

export type DiagnosticSeverity = 'Info' | 'Warning' | 'Error' | 'Fatal';

export interface Diagnostic {
  code: string;
  severity: DiagnosticSeverity;
  message: string;
  relativePath?: string;
  line?: number;
  column?: number;
  adapter: string;
  recoverable: boolean;
  affectsCompleteness: boolean;
}

export interface GraphNode {
  id: string;
  kind: NodeKind;
  language: string;
  framework?: string | null;
  analysisLevel: CapabilityLevel;
  displayName: string;
  qualifiedName: string;
  relativePath: string;
  projectOrPackage?: string | null;
  sourceAvailable: boolean;
  generated: boolean;
  testFile: boolean;
  depth: number;
  importance: number;
  diagnostics: Diagnostic[];
  metadata: Record<string, any>;
  content?: string;
}

export interface GraphEdge {
  sourceNodeId: string;
  targetNodeId: string;
  relationship: RelationshipType;
  confidence: Confidence;
  analysisLevel: CapabilityLevel;
  sourceLocation?: { line?: number; column?: number };
  metadata: Record<string, any>;
}

export interface DependencyGraphFragment {
  nodes: GraphNode[];
  edges: GraphEdge[];
  diagnostics?: Diagnostic[];
  warnings?: string[];
  dynamicConstructs?: string[];
}

export type CommentMode = 'Preserve' | 'Remove' | 'Auto';

export type LanguageName =
  | 'Auto'
  | 'CSharp'
  | 'VisualBasic'
  | 'TypeScript'
  | 'JavaScript'
  | 'JSX'
  | 'TSX'
  | 'HTML'
  | 'CSS'
  | 'SCSS'
  | 'Sass'
  | 'Java'
  | 'Python'
  | 'Go'
  | 'Razor'
  | 'Vue';

export type FrameworkName =
  | 'Auto'
  | 'None'
  | 'AspNetCore'
  | 'RazorPages'
  | 'Blazor'
  | 'Node'
  | 'React'
  | 'Angular'
  | 'Vue';

export type RequestedAnalysisLevel = 'Best' | 'Semantic' | 'SyntaxAware' | 'ImportGraph';

export type ExclusionReason =
  | 'TokenBudget'
  | 'MaxDepth'
  | 'TestFileExcluded'
  | 'GeneratedFileExcluded'
  | 'AssetExcluded'
  | 'ConfigurationFileExcluded'
  | 'ExcludedProject'
  | 'ExcludedPackage'
  | 'ExcludedNamespace'
  | 'ExcludeGlob'
  | 'NoSourceDeclaration'
  | 'ExternalDependency'
  | 'UnsupportedDocument'
  | 'DuplicatePhysicalFile'
  | 'UnresolvedDependency'
  | 'OutsideWorkspace'
  | 'SecurityRestriction';

export type ErrorCode =
  | 'FileNotFound'
  | 'WorkspaceNotFound'
  | 'ProjectNotFound'
  | 'UnsupportedLanguage'
  | 'UnsupportedFramework'
  | 'AdapterNotInstalled'
  | 'AdapterVersionMismatch'
  | 'WorkerStartFailed'
  | 'WorkerProtocolError'
  | 'DocumentNotInWorkspace'
  | 'NoRootSymbol'
  | 'MultipleRootSymbols'
  | 'RootSymbolNotFound'
  | 'WorkspaceLoadFailed'
  | 'RestoreRequired'
  | 'BuildToolNotFound'
  | 'InvalidConfiguration'
  | 'GenerationCancelled'
  | 'OutputWriteFailed'
  | 'ClipboardFailed'
  | 'OutsideWorkspace'
  | 'UntrustedWorkspace'
  | 'TokenBudgetTooSmall'
  | 'CommentRemovalUnsupported'
  | 'UnexpectedError';

export interface SecretFinding {
  relativePath: string;
  line: number;
  category: string;
  recommendation: string;
}

export interface UserConfiguration {
  rootPath: string;
  workspacePath?: string;
  projectPath?: string;
  rootSymbol?: string;
  outputPath?: string;

  language: LanguageName;
  framework: FrameworkName;
  analysisLevel: RequestedAnalysisLevel;

  maxTokens?: number | null;
  maxDepth?: number | null;
  commentMode: CommentMode;

  includeTests: boolean;
  includeGeneratedFiles: boolean;
  includeAttributes: boolean;
  includeImplementations: boolean;
  includeCompanionFiles: boolean;
  includeAssets: boolean;
  includeConfigurationFiles: boolean;
  includeMetadataDependenciesInTree: boolean;
  includeDependencyTree: boolean;
  includeExcludedFileList: boolean;
  includeGenerationMetadata: boolean;

  copyToClipboard: boolean;
  saveToFile: boolean;
  openAfterGeneration: boolean;

  task?: string;

  includeGlobs: string[];
  excludeGlobs: string[];
  excludedProjects: string[];
  excludedPackages: string[];
  excludedNamespaces: string[];
  excludedDirectories: string[];

  tokenizer: string;
  verbose: boolean;
  omitTimestamp?: boolean;
}

export interface WorkerProtocolRequest {
  protocolVersion: string;
  requestId: string;
  operation: 'analyse' | 'detect' | 'removeComments' | 'ping' | 'capabilities';
  rootPath?: string;
  filePath?: string;
  workspacePath: string;
  language?: string;
  framework?: string;
  code?: string;
  options?: Record<string, any>;
}

export interface WorkerAdapterDescriptor {
  id: string;
  version: string;
  capability: CapabilityLevel;
}

export interface DetectedFramework {
  id: string;
  confidence: Confidence;
}

export interface WorkerProtocolResponse {
  protocolVersion: string;
  requestId: string;
  success: boolean;
  adapter?: WorkerAdapterDescriptor;
  frameworks?: DetectedFramework[];
  graph?: DependencyGraphFragment;
  code?: string;
  diagnostics?: Diagnostic[];
  warnings?: string[];
  errorCode?: ErrorCode;
  errorMessage?: string;
}
