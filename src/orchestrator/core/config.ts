import { UserConfiguration, CommentMode, LanguageName, FrameworkName, RequestedAnalysisLevel } from '../protocol/types.js';

export const DEFAULT_CONFIGURATION: UserConfiguration = {
  rootPath: '',
  workspacePath: undefined,
  projectPath: undefined,
  rootSymbol: undefined,
  outputPath: undefined,

  language: 'Auto',
  framework: 'Auto',
  analysisLevel: 'Best',

  maxTokens: null,
  maxDepth: null,
  commentMode: 'Preserve',

  includeTests: false,
  includeGeneratedFiles: false,
  includeAttributes: false,
  includeImplementations: false,
  includeCompanionFiles: true,
  includeAssets: false,
  includeConfigurationFiles: true,
  includeMetadataDependenciesInTree: true,
  includeDependencyTree: true,
  includeExcludedFileList: true,
  includeGenerationMetadata: true,

  copyToClipboard: false,
  saveToFile: true,
  openAfterGeneration: false,

  task: undefined,

  includeGlobs: [],
  excludeGlobs: [],
  excludedProjects: [],
  excludedPackages: [],
  excludedNamespaces: [],
  excludedDirectories: [],

  tokenizer: 'approximate',
  verbose: false,
  omitTimestamp: false,
};

export function resolveConfiguration(overrides: Partial<UserConfiguration>): UserConfiguration {
  const config: UserConfiguration = {
    ...DEFAULT_CONFIGURATION,
    ...overrides,
    includeGlobs: [...(overrides.includeGlobs || DEFAULT_CONFIGURATION.includeGlobs)],
    excludeGlobs: [...(overrides.excludeGlobs || DEFAULT_CONFIGURATION.excludeGlobs)],
    excludedProjects: [...(overrides.excludedProjects || DEFAULT_CONFIGURATION.excludedProjects)],
    excludedPackages: [...(overrides.excludedPackages || DEFAULT_CONFIGURATION.excludedPackages)],
    excludedNamespaces: [...(overrides.excludedNamespaces || DEFAULT_CONFIGURATION.excludedNamespaces)],
    excludedDirectories: [...(overrides.excludedDirectories || DEFAULT_CONFIGURATION.excludedDirectories)],
  };

  // Validation
  if (!config.rootPath || config.rootPath.trim().length === 0) {
    throw new Error('Root path (--file) must be provided.');
  }

  if (!config.saveToFile && !config.copyToClipboard) {
    throw new Error('Invalid configuration: At least one of save (--save) or copy (--copy) must be true.');
  }

  if (config.maxTokens !== null && config.maxTokens !== undefined && config.maxTokens <= 0) {
    throw new Error('Token budget (--max-tokens) must be a positive integer.');
  }

  if (config.maxDepth !== null && config.maxDepth !== undefined && config.maxDepth < 0) {
    throw new Error('Maximum depth (--max-depth) cannot be negative.');
  }

  return config;
}
