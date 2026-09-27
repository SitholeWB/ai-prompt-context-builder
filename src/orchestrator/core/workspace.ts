import * as fs from 'node:fs';
import * as path from 'node:path';

export interface DiscoveredWorkspace {
  workspaceRoot: string;
  projectRoot?: string;
  projectFiles: string[];
  repoIndicators: string[];
  frameworkIndicators: string[];
  isMonorepo: boolean;
  packageRoots: string[];
}

const REPO_ROOT_INDICATORS = ['.git', '.sln', '.slnx', 'go.work'];

const PROJECT_INDICATORS = [
  'Directory.Build.props',
  'Directory.Packages.props',
  'package.json',
  'package-lock.json',
  'yarn.lock',
  'pnpm-lock.yaml',
  'bun.lock',
  'tsconfig.json',
  'jsconfig.json',
  'angular.json',
  'pom.xml',
  'build.gradle',
  'build.gradle.kts',
  'settings.gradle',
  'settings.gradle.kts',
  'pyproject.toml',
  'setup.py',
  'setup.cfg',
  'requirements.txt',
  'Pipfile',
  'poetry.lock',
  'go.mod',
];

const FRAMEWORK_CONFIG_PREFIXES = [
  'vite.config',
  'webpack.config',
  'vue.config',
  'next.config',
  'nuxt.config',
  'svelte.config',
  'angular.json',
];

export function discoverWorkspace(
  sourceFilePath: string,
  explicitWorkspacePath?: string,
  explicitProjectPath?: string
): DiscoveredWorkspace {
  const resolvedSource = path.resolve(sourceFilePath);
  let currentDir = fs.existsSync(resolvedSource) && fs.statSync(resolvedSource).isDirectory()
    ? resolvedSource
    : path.dirname(resolvedSource);

  const boundary = explicitWorkspacePath
    ? path.resolve(explicitWorkspacePath)
    : path.parse(currentDir).root;

  const discoveredProjects: string[] = [];
  const discoveredRepo: string[] = [];
  const discoveredFrameworks: string[] = [];
  const packageRoots: string[] = [];
  let detectedWorkspaceRoot: string | undefined = explicitWorkspacePath ? path.resolve(explicitWorkspacePath) : undefined;
  let detectedProjectRoot: string | undefined = explicitProjectPath ? path.resolve(explicitProjectPath) : undefined;

  let dir = currentDir;
  while (true) {
    let entries: string[] = [];
    try {
      entries = fs.readdirSync(dir);
    } catch {
      // Permission or missing dir
    }

    const hasRepoIndicator = entries.some((e) => REPO_ROOT_INDICATORS.includes(e));
    if (hasRepoIndicator && !detectedWorkspaceRoot) {
      detectedWorkspaceRoot = dir;
      discoveredRepo.push(...entries.filter((e) => REPO_ROOT_INDICATORS.includes(e)));
    }

    const matchingProjects = entries.filter((e) => {
      if (PROJECT_INDICATORS.includes(e)) return true;
      if (e.endsWith('.csproj') || e.endsWith('.vbproj') || e.endsWith('.fsproj')) return true;
      if (e.endsWith('.sln') || e.endsWith('.slnx')) return true;
      return false;
    });

    if (matchingProjects.length > 0) {
      if (!detectedProjectRoot) {
        detectedProjectRoot = dir;
      }
      for (const p of matchingProjects) {
        const fullPath = path.join(dir, p);
        if (!discoveredProjects.includes(fullPath)) {
          discoveredProjects.push(fullPath);
        }
      }
      if (entries.includes('package.json') && !packageRoots.includes(dir)) {
        packageRoots.push(dir);
      }
    }

    const matchingFw = entries.filter((e) =>
      FRAMEWORK_CONFIG_PREFIXES.some((prefix) => e.startsWith(prefix))
    );
    for (const f of matchingFw) {
      discoveredFrameworks.push(path.join(dir, f));
    }

    if (dir === boundary || dir === path.dirname(dir)) {
      break;
    }
    dir = path.dirname(dir);
  }

  // If no workspace was detected, fallback to project root or current dir
  const finalWorkspaceRoot = detectedWorkspaceRoot || detectedProjectRoot || currentDir;
  const isMonorepo = packageRoots.length > 1 || entriesIndicateMonorepo(finalWorkspaceRoot);

  return {
    workspaceRoot: finalWorkspaceRoot,
    projectRoot: detectedProjectRoot,
    projectFiles: discoveredProjects,
    repoIndicators: discoveredRepo,
    frameworkIndicators: discoveredFrameworks,
    isMonorepo,
    packageRoots,
  };
}

function entriesIndicateMonorepo(dirPath: string): boolean {
  try {
    const pkgJsonPath = path.join(dirPath, 'package.json');
    if (fs.existsSync(pkgJsonPath)) {
      const content = JSON.parse(fs.readFileSync(pkgJsonPath, 'utf8'));
      if (content.workspaces) return true;
    }
    if (fs.existsSync(path.join(dirPath, 'pnpm-workspace.yaml'))) return true;
    if (fs.existsSync(path.join(dirPath, 'go.work'))) return true;
  } catch {
    // Ignore JSON parse errors
  }
  return false;
}
