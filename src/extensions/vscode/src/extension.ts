import * as vscode from 'vscode';
import { spawn, execSync } from 'child_process';
import * as path from 'path';
import * as fs from 'fs';

type GitScopeType = 'none' | 'all' | 'staged' | 'unstaged' | 'file' | 'branch' | 'commits';

let lastGeneratedPath: string | null = null;
let outputChannel: vscode.OutputChannel;
let extContext: vscode.ExtensionContext;
let historyTreeProvider: HistoryTreeProvider;

function resolveTargetFilePath(uri?: vscode.Uri): string | undefined {
  const candidate = uri?.fsPath || vscode.window.activeTextEditor?.document.uri.fsPath;
  if (!candidate) return undefined;
  try {
    if (fs.existsSync(candidate) && fs.statSync(candidate).isDirectory()) {
      return vscode.window.activeTextEditor?.document.uri.fsPath;
    }
  } catch {
    // ignore stat errors
  }
  return candidate;
}

export function activate(context: vscode.ExtensionContext) {
  extContext = context;
  outputChannel = vscode.window.createOutputChannel('AI Context & Prompt Builder');
  context.subscriptions.push(outputChannel);

  // Register Activity Bar Sidebar Views
  const actionsProvider = new ActionsTreeProvider();
  historyTreeProvider = new HistoryTreeProvider(context);
  context.subscriptions.push(
    vscode.window.registerTreeDataProvider('aiPromptContextBuilder.actionsView', actionsProvider),
    vscode.window.registerTreeDataProvider('aiPromptContextBuilder.historyView', historyTreeProvider)
  );

  const registerCmd = (name: string, handler: (...args: any[]) => any) => {
    context.subscriptions.push(vscode.commands.registerCommand(`aiPromptContextBuilder.${name}`, handler));
    // Register backwards-compatible alias
    context.subscriptions.push(vscode.commands.registerCommand(`aiContextBuilder.${name}`, handler));
  };

  // 1. Single Right-Click & Interactive Popup Entry Point
  registerCmd('generateFromFile', async (uri?: vscode.Uri) => {
    const filePath = resolveTargetFilePath(uri);
    await showScopeAndExportPopups(filePath, false);
  });

  // 1b. Generate from Git Changes (Opens Interactive Popup with Git options first)
  registerCmd('generateFromGit', async (uri?: vscode.Uri) => {
    const filePath = resolveTargetFilePath(uri);
    await showScopeAndExportPopups(filePath, true);
  });

  // 1c. Sidebar Quick-Scope Trigger (Opens Popup 2 & Popup 3 for a chosen Git scope)
  registerCmd('generateWithScopePopup', async (preselectedScope: GitScopeType) => {
    const filePath = resolveTargetFilePath();
    await showScopeAndExportPopups(filePath, true, preselectedScope);
  });

  // 1d. Open a specific file from the Sidebar History View
  registerCmd('openHistoryFile', async (targetPath: string) => {
    if (targetPath && fs.existsSync(targetPath)) {
      const doc = await vscode.workspace.openTextDocument(targetPath);
      await vscode.window.showTextDocument(doc);
    } else {
      vscode.window.showWarningMessage(`File no longer exists on disk: ${targetPath}`);
      historyTreeProvider.refresh();
    }
  });

  // 2. Generate from Symbol at Cursor
  registerCmd('generateFromSymbol', async () => {
    const editor = vscode.window.activeTextEditor;
    if (!editor) {
      vscode.window.showErrorMessage('No active editor open.');
      return;
    }
    const filePath = editor.document.uri.fsPath;
    const selection = editor.selection;
    let symbol = editor.document.getText(selection).trim();

    if (!symbol) {
      const wordRange = editor.document.getWordRangeAtPosition(selection.active);
      if (wordRange) {
        symbol = editor.document.getText(wordRange).trim();
      }
    }

    if (!symbol) {
      vscode.window.showErrorMessage('No symbol found at cursor position.');
      return;
    }

    await runGeneration(filePath, { symbol });
  });

  // 3. Generate with Options
  registerCmd('generateWithOptions', async (uri?: vscode.Uri) => {
    const filePath = uri?.fsPath || vscode.window.activeTextEditor?.document.uri.fsPath;
    if (!filePath) {
      vscode.window.showErrorMessage('No active file selected.');
      return;
    }
    await promptAndRunCustomOptions(filePath);
  });

  // 4. Generate and Copy
  registerCmd('generateAndCopy', async () => {
    const filePath = vscode.window.activeTextEditor?.document.uri.fsPath;
    if (!filePath) return;
    await runGeneration(filePath, { copyToClipboard: true });
  });

  // 5. Generate and Save
  registerCmd('generateAndSave', async () => {
    const filePath = vscode.window.activeTextEditor?.document.uri.fsPath;
    if (!filePath) return;
    await runGeneration(filePath, { saveToFile: true });
  });

  // 6. Open Last Generated
  registerCmd('openLastGenerated', async () => {
    if (lastGeneratedPath && fs.existsSync(lastGeneratedPath)) {
      const doc = await vscode.workspace.openTextDocument(lastGeneratedPath);
      await vscode.window.showTextDocument(doc);
    } else {
      vscode.window.showInformationMessage('No generated prompt context file found.');
    }
  });

  // 7. Show Dependency Preview
  registerCmd('showDependencyPreview', async () => {
    if (lastGeneratedPath && fs.existsSync(lastGeneratedPath)) {
      const content = fs.readFileSync(lastGeneratedPath, 'utf8');
      const treeMatch = content.match(/## Dependency Tree\s+```text([\s\S]*?)```/);
      if (treeMatch) {
        vscode.window.showInformationMessage(treeMatch[1].trim());
      }
    }
  });

  // 8. Show Output Log
  registerCmd('showOutputLog', () => {
    outputChannel.show();
  });

  // 9. Check Language Workers
  registerCmd('checkWorkers', async () => {
    const spec = resolveCommand(['check-workers']);
    outputChannel.appendLine(`Checking workers using ${spec.description}...`);
    const proc = spawn(spec.command, spec.args, spec.options);

    let output = '';
    let stderr = '';
    proc.stdout.on('data', (d) => { output += d.toString(); });
    proc.stderr.on('data', (d) => { stderr += d.toString(); });

    proc.on('error', async (err) => {
      outputChannel.appendLine(`Failed to launch '${spec.command}': ${err.message}`);
      await handleMissingRuntime(spec.command, err);
    });

    proc.on('close', (code) => {
      if (output) outputChannel.appendLine(output);
      if (stderr) outputChannel.appendLine(stderr);
      outputChannel.show();
      if (code === 0) {
        vscode.window.showInformationMessage(`Language workers verified successfully (exit code: ${code}). See output log.`);
      } else {
        vscode.window.showErrorMessage(`Worker check failed with exit code ${code}. See output log for details.`);
      }
    });
  });

  // 10. Auto-Install / Acquire .NET Runtime
  registerCmd('acquireRuntime', async () => {
    await installDotnetViaExtension();
  });
}

function getConfiguration(): vscode.WorkspaceConfiguration {
  const primary = vscode.workspace.getConfiguration('aiPromptContextBuilder');
  if (primary.has('executablePath')) {
    return primary;
  }
  return vscode.workspace.getConfiguration('aiContextBuilder');
}

interface CommandSpec {
  command: string;
  args: string[];
  options: {
    shell?: boolean;
    cwd?: string;
  };
  description: string;
}

function resolveCommand(extraArgs: string[]): CommandSpec {
  const config = getConfiguration();
  const configuredExe = config.get<string>('executablePath')?.trim();

  // 1. If user explicitly provided a custom path and it exists:
  if (configuredExe && path.isAbsolute(configuredExe) && fs.existsSync(configuredExe)) {
    if (configuredExe.toLowerCase().endsWith('.dll')) {
      return {
        command: 'dotnet',
        args: [configuredExe, ...extraArgs],
        options: {},
        description: configuredExe,
      };
    }
    return {
      command: configuredExe,
      args: extraArgs,
      options: { shell: process.platform === 'win32' },
      description: configuredExe,
    };
  }

  // 2. Check bundled portable .NET assembly inside the extension itself!
  if (extContext && extContext.extensionPath) {
    const bundledDll = path.join(extContext.extensionPath, 'bin', 'aipromptcontext.dll');
    if (fs.existsSync(bundledDll)) {
      const acquiredDotnet = extContext.globalState.get<string>('acquiredDotnetPath');
      const dotnetHost = (acquiredDotnet && fs.existsSync(acquiredDotnet)) ? acquiredDotnet : 'dotnet';
      return {
        command: dotnetHost,
        args: [bundledDll, ...extraArgs],
        options: {},
        description: `bundled assembly (${bundledDll}) via ${dotnetHost}`,
      };
    }

    // On Windows, check bundled .cmd wrapper
    if (process.platform === 'win32') {
      const bundledCmd = path.join(extContext.extensionPath, 'bin', 'aipromptcontext.cmd');
      if (fs.existsSync(bundledCmd)) {
        return {
          command: bundledCmd,
          args: extraArgs,
          options: { shell: true },
          description: `bundled wrapper (${bundledCmd})`,
        };
      }
    }
  }

  // 3. Check workspace folders for ./bin/
  if (vscode.workspace.workspaceFolders) {
    for (const folder of vscode.workspace.workspaceFolders) {
      const candidates = [
        path.join(folder.uri.fsPath, 'bin', 'aipromptcontext.dll'),
        path.join(folder.uri.fsPath, 'bin', 'aipromptcontext.cmd'),
        path.join(folder.uri.fsPath, 'bin', 'aipromptcontext.exe'),
        path.join(folder.uri.fsPath, 'bin', 'aipromptcontext'),
        path.join(folder.uri.fsPath, 'bin', 'aicontext.dll'),
        path.join(folder.uri.fsPath, 'bin', 'aicontext.cmd'),
        path.join(folder.uri.fsPath, 'bin', 'aicontext'),
      ];
      for (const cand of candidates) {
        if (fs.existsSync(cand)) {
          if (cand.toLowerCase().endsWith('.dll')) {
            return {
              command: 'dotnet',
              args: [cand, ...extraArgs],
              options: {},
              description: `workspace ${cand}`,
            };
          }
          return {
            command: cand,
            args: extraArgs,
            options: { shell: process.platform === 'win32' },
            description: `workspace ${cand}`,
          };
        }
      }
    }
  }

  // 4. Check known development shims (for local repo testing)
  const devShims = [
    '/home/wb-sithole/.gemini/antigravity/scratch/ai-context-builder/bin/aipromptcontext',
    '/home/wb-sithole/.gemini/antigravity/scratch/ai-context-builder/bin/aicontext',
    '/home/wb-sithole/.gemini/antigravity/scratch/ai-prompt-context-builder/bin/aipromptcontext',
    '/home/wb-sithole/.gemini/antigravity/scratch/ai-prompt-context-builder/bin/aicontext',
  ];
  for (const shim of devShims) {
    if (fs.existsSync(shim)) {
      return {
        command: shim,
        args: extraArgs,
        options: {},
        description: `dev shim (${shim})`,
      };
    }
  }

  // 5. Fallback to system command in PATH
  const fallback = configuredExe || 'aipromptcontext';
  return {
    command: fallback,
    args: extraArgs,
    options: { shell: process.platform === 'win32' },
    description: fallback,
  };
}

function getGitBranches(cwd?: string): string[] {
  if (!cwd) return ['main', 'master', 'develop'];
  try {
    const out = execSync('git branch -a --format="%(refname:short)"', {
      cwd,
      encoding: 'utf8',
      stdio: ['ignore', 'pipe', 'ignore'],
    });
    const raw = out
      .split(/\r?\n/)
      .map((s) => s.trim())
      .filter((s) => s && !s.includes('HEAD') && s !== 'origin');
    const priority = ['main', 'master', 'develop', 'origin/main', 'origin/master', 'origin/develop'];
    const sorted = [
      ...priority.filter((p) => raw.includes(p)),
      ...raw.filter((r) => !priority.includes(r)),
    ];
    return sorted.length > 0 ? Array.from(new Set(sorted)) : ['main', 'master', 'develop'];
  } catch {
    return ['main', 'master', 'develop'];
  }
}

function getRecentCommitsPreview(cwd?: string, count = 10): string[] {
  if (!cwd) return [];
  try {
    const out = execSync(`git log -n ${count} --oneline`, {
      cwd,
      encoding: 'utf8',
      stdio: ['ignore', 'pipe', 'ignore'],
    });
    return out
      .split(/\r?\n/)
      .map((s) => s.trim())
      .filter(Boolean);
  } catch {
    return [];
  }
}

async function promptForTargetBranch(workspaceFolder?: string): Promise<string | undefined> {
  const branches = getGitBranches(workspaceFolder);
  const items: vscode.QuickPickItem[] = branches.map((b) => ({
    label: `$(git-branch) ${b}`,
    description: b.startsWith('origin/') ? 'Remote Branch' : 'Local Branch',
    detail: `Compare current HEAD against '${b}' (${b}...HEAD) + resolve all modified files' dependency trees`,
  }));
  items.push({
    label: '$(edit) Custom Branch, Tag, or Commit SHA...',
    description: 'Enter manually',
    detail: 'Type any Git branch name, tag, or commit reference',
  });

  const pick = await vscode.window.showQuickPick(items, {
    title: 'Compare Branch / Pull Request Scope',
    placeHolder: 'Select the base branch to compare against (e.g. main, develop, origin/main)',
  });
  if (!pick) return undefined;

  if (pick.label.includes('Custom Branch')) {
    const custom = await vscode.window.showInputBox({
      prompt: 'Enter target branch name, tag, or commit SHA to compare against',
      placeHolder: 'e.g. main, origin/main, v1.0.0, or a1b2c3d',
    });
    return custom?.trim() || undefined;
  }

  return pick.label.replace('$(git-branch) ', '').trim();
}

async function promptForCommitCount(workspaceFolder?: string): Promise<number | undefined> {
  const recentCommits = getRecentCommitsPreview(workspaceFolder, 10);
  const latest1 = recentCommits[0] ? `Latest: ${recentCommits[0]}` : 'HEAD~1..HEAD';
  const latest2 = recentCommits.slice(0, 2).join(' | ') || 'HEAD~2..HEAD';
  const latest3 = recentCommits.slice(0, 3).join(' | ') || 'HEAD~3..HEAD';
  const latest5 = recentCommits.slice(0, 5).join(' | ') || 'HEAD~5..HEAD';

  const items = [
    {
      label: '$(git-commit) Last 1 Commit',
      description: 'HEAD~1..HEAD',
      detail: latest1,
      count: 1,
    },
    {
      label: '$(git-commit) Last 2 Commits',
      description: 'HEAD~2..HEAD',
      detail: latest2,
      count: 2,
    },
    {
      label: '$(git-commit) Last 3 Commits',
      description: 'HEAD~3..HEAD',
      detail: latest3,
      count: 3,
    },
    {
      label: '$(git-commit) Last 5 Commits',
      description: 'HEAD~5..HEAD',
      detail: latest5,
      count: 5,
    },
    {
      label: '$(git-commit) Last 10 Commits',
      description: 'HEAD~10..HEAD',
      detail: 'All files modified in the last 10 commits + their dependency trees',
      count: 10,
    },
    {
      label: '$(edit) Custom Number of Commits...',
      description: 'Enter integer N',
      detail: 'Analyze files modified across the last N commits (HEAD~N..HEAD)',
      count: -1,
    },
  ];

  const pick = await vscode.window.showQuickPick(items, {
    title: 'Last X Commits + Dependencies Tree',
    placeHolder: 'Select how many recent commits to include in the AI context',
  });
  if (!pick) return undefined;

  if (pick.count === -1) {
    const val = await vscode.window.showInputBox({
      prompt: 'Enter number of recent commits to analyze (e.g. 4)',
      placeHolder: '4',
    });
    const parsed = val ? parseInt(val.trim(), 10) : NaN;
    return !isNaN(parsed) && parsed > 0 ? parsed : undefined;
  }

  return pick.count;
}

async function showScopeAndExportPopups(
  filePath: string | undefined,
  gitFirst: boolean,
  preselectedScope?: GitScopeType
) {
  const workspaceFolder =
    vscode.workspace.workspaceFolders?.[0]?.uri.fsPath ||
    (filePath ? path.dirname(filePath) : undefined);
  if (!filePath && !workspaceFolder) {
    vscode.window.showErrorMessage('Please open a file or workspace folder to generate AI context.');
    return;
  }

  interface ScopeQuickPickItem extends vscode.QuickPickItem {
    gitScope: GitScopeType;
  }

  const fileItem: ScopeQuickPickItem | undefined = filePath
    ? {
        label: '$(file-code) Dependencies Only (No Git Changes)',
        description: `Classic Mode (${path.basename(filePath)})`,
        detail: 'Trace full dependency tree of the selected file without Git diffs',
        gitScope: 'none',
      }
    : undefined;

  const singleFileGitItem: ScopeQuickPickItem | undefined = filePath
    ? {
        label: '$(diff) Single File Git Changes + Dependencies Tree',
        description: `Current File (${path.basename(filePath)})`,
        detail: 'Trace dependency tree of the selected file and include its Git diff',
        gitScope: 'file',
      }
    : undefined;

  const gitItems: ScopeQuickPickItem[] = [
    {
      label: '$(git-merge) All Git Changes (Staged + Unstaged) + Dependencies Tree',
      description: 'Working Tree Scope',
      detail: 'Discover all modified, added & untracked source files and trace their combined dependency trees',
      gitScope: 'all',
    },
    {
      label: '$(diff-added) Staged Git Changes ONLY + Dependencies Tree',
      description: 'git diff --cached',
      detail: 'Discover only staged (git add) files and trace their combined dependency trees',
      gitScope: 'staged',
    },
    {
      label: '$(diff-modified) Unstaged Git Changes ONLY + Dependencies Tree',
      description: 'git diff',
      detail: 'Discover only unstaged working tree modifications and trace their combined dependency trees',
      gitScope: 'unstaged',
    },
    {
      label: '$(git-pull-request) Compare Branch / Pull Request + Dependencies Tree...',
      description: 'Branch / PR Diff',
      detail: 'Compare current branch against main/develop/origin and trace dependency trees of all PR files',
      gitScope: 'branch',
    },
    {
      label: '$(git-commit) Last X Commits + Dependencies Tree...',
      description: 'Recent Commits (HEAD~N)',
      detail: 'Select last 1, 2, 3, 5, or N commits and trace dependency trees of all touched files',
      gitScope: 'commits',
    },
  ];

  const scopeItems: ScopeQuickPickItem[] = [];
  if (gitFirst) {
    scopeItems.push(...gitItems);
    if (singleFileGitItem) scopeItems.push(singleFileGitItem);
    if (fileItem) scopeItems.push(fileItem);
  } else {
    if (fileItem) scopeItems.push(fileItem);
    scopeItems.push(...gitItems);
    if (singleFileGitItem) scopeItems.push(singleFileGitItem);
  }

  let chosenScope = preselectedScope;
  let chosenDescription = preselectedScope || '';

  if (chosenScope && (chosenScope === 'none' || chosenScope === 'file') && !filePath) {
    vscode.window.showErrorMessage('Please open a source file in the editor first, or choose a Workspace Git Scope.');
    return;
  }

  // Popup 1: Select Scope (unless preselected from a specific sidebar shortcut)
  if (!chosenScope) {
    const scopePick = await vscode.window.showQuickPick(scopeItems, {
      title: filePath
        ? `AI Prompt & Context: ${path.basename(filePath)}`
        : 'AI Prompt & Context: Git & Workspace Scopes',
      placeHolder: 'Step 1: Select Context Scope (File Dependencies, Git Working Tree, Branch/PR, or Last X Commits)',
    });
    if (!scopePick) return;
    chosenScope = scopePick.gitScope;
    chosenDescription = scopePick.description || scopePick.label;
  } else {
    const matched = scopeItems.find((i) => i.gitScope === chosenScope);
    chosenDescription = matched?.description || chosenScope;
  }

  let compareBranch: string | undefined;
  let commitCount: number | undefined;

  if (chosenScope === 'branch') {
    compareBranch = await promptForTargetBranch(workspaceFolder);
    if (!compareBranch) return;
    chosenDescription = `Compare vs ${compareBranch}`;
  } else if (chosenScope === 'commits') {
    commitCount = await promptForCommitCount(workspaceFolder);
    if (!commitCount) return;
    chosenDescription = `Last ${commitCount} Commit(s)`;
  }

  let includeGitDiff = true;

  // Popup 2: Select Output Content (only when a Git scope is chosen)
  if (chosenScope !== 'none') {
    const diffPick = await vscode.window.showQuickPick(
      [
        {
          label: '$(diff) Git Diff + Full Dependencies Tree',
          description: 'Recommended',
          detail: 'Includes unified (+ / -) Git diff patch at top AND full source code of modified files + dependencies',
          includeDiff: true,
        },
        {
          label: '$(list-tree) Dependencies Tree Only (No Diff Text)',
          description: 'Compact',
          detail: 'Includes full source code of Git-modified files and their dependency trees, skipping raw diff text',
          includeDiff: false,
        },
      ],
      {
        title: `Git Context Content (${chosenDescription})`,
        placeHolder: 'Step 2: Choose whether to include the unified (+ / -) Git diff block in the Markdown',
      }
    );
    if (!diffPick) return;
    includeGitDiff = diffPick.includeDiff;
  }

  // Popup 3: Export with Defaults vs Custom Options
  const modePick = await vscode.window.showQuickPick(
    [
      {
        label: '$(zap) Export with Defaults',
        description: 'Instant Generation (Recommended)',
        detail: 'Full dependency graph, automatic token budget, comments preserved, tools/lockfiles excluded',
      },
      {
        label: '$(gear) Custom Options...',
        description: 'Step-by-Step Configuration',
        detail: 'Configure token budget, comment stripping, traversal depth, output destination & custom prompt',
      },
    ],
    {
      title: 'Choose Export Mode',
      placeHolder: 'Instant Defaults or Step-by-Step Custom Options',
    }
  );
  if (!modePick) return;

  const baseOptions: RunOptions = {
    gitScope: chosenScope,
    includeGitDiff,
    workspacePath: workspaceFolder,
    compareBranch,
    commitCount,
  };

  const targetPath = filePath || workspaceFolder || '';

  if (modePick.label.includes('Defaults')) {
    await runGeneration(targetPath, baseOptions);
  } else {
    await promptAndRunCustomOptions(targetPath, baseOptions);
  }
}

async function promptAndRunCustomOptions(filePath: string, baseOptions: RunOptions = {}) {
  // Step 1: Token Budget
  const budgetPick = await vscode.window.showQuickPick(
    [
      { label: 'Unlimited', description: 'Include all eligible transitive dependencies (Full context)' },
      { label: '32,000', description: '32k token budget (Fast / Compact)' },
      { label: '64,000', description: '64k token budget (Standard Claude / GPT-4)' },
      { label: '100,000', description: '100k token budget' },
      { label: '128,000', description: '128k token budget' },
      { label: '200,000', description: '200k token budget (Large models)' },
      { label: 'Custom', description: 'Enter specific integer token budget' },
    ],
    { title: 'Step 1/5: Select Token Budget' }
  );
  if (!budgetPick) return;

  let maxTokens: number | undefined;
  if (budgetPick.label === 'Custom') {
    const customVal = await vscode.window.showInputBox({ prompt: 'Enter token budget (e.g. 50000)' });
    if (customVal && parseInt(customVal, 10)) {
      maxTokens = parseInt(customVal, 10);
    }
  } else if (budgetPick.label !== 'Unlimited') {
    maxTokens = parseInt(budgetPick.label.replace(',', ''), 10);
  }

  // Step 2: Comment Mode
  const commentPick = await vscode.window.showQuickPick(
    [
      { label: 'Preserve', description: 'Retain original comments & docstrings (Default)' },
      { label: 'Auto', description: 'Remove comments only if token budget is exceeded' },
      { label: 'Remove', description: 'Remove comments to maximize code tokens' },
    ],
    { title: 'Step 2/5: Select Comment Mode' }
  );
  if (!commentPick) return;

  // Step 3: Traversal Depth
  const depthPick = await vscode.window.showQuickPick(
    [
      { label: 'Unlimited', description: 'Traverse full transitive dependency tree (Default)' },
      { label: '1', description: 'Direct dependencies only' },
      { label: '2', description: 'Depth 2 (Direct dependencies + their immediate imports)' },
      { label: '3', description: 'Depth 3' },
      { label: '5', description: 'Depth 5' },
      { label: 'Custom', description: 'Enter custom integer depth' },
    ],
    { title: 'Step 3/5: Select Traversal Depth' }
  );
  if (!depthPick) return;

  let maxDepth: number | undefined;
  if (depthPick.label === 'Custom') {
    const val = await vscode.window.showInputBox({ prompt: 'Enter max traversal depth (integer)' });
    if (val && parseInt(val, 10)) maxDepth = parseInt(val, 10);
  } else if (depthPick.label !== 'Unlimited') {
    maxDepth = parseInt(depthPick.label, 10);
  }

  // Step 4: Output Destination
  const destPick = await vscode.window.showQuickPick(
    [
      { label: 'Save to File', description: 'Save Markdown in .ai-context/ folder (Default)' },
      { label: 'Copy to Clipboard', description: 'Copy generated prompt directly to clipboard' },
      { label: 'Both', description: 'Save to file and copy to clipboard' },
    ],
    { title: 'Step 4/5: Select Output Destination' }
  );
  if (!destPick) return;

  let saveToFile = true;
  let copyToClipboard = false;
  if (destPick.label === 'Copy to Clipboard') {
    saveToFile = false;
    copyToClipboard = true;
  } else if (destPick.label === 'Both') {
    saveToFile = true;
    copyToClipboard = true;
  }

  // Step 5: Optional Custom Task Prompt
  const taskInput = await vscode.window.showInputBox({
    prompt: 'Step 5/5: (Optional) Enter custom instructions or goal for the AI prompt',
    placeHolder: 'e.g. Refactor this service to use asynchronous patterns, or Add unit tests'
  });

  await runGeneration(filePath, {
    ...baseOptions,
    maxTokens,
    commentMode: commentPick.label,
    maxDepth,
    saveToFile,
    copyToClipboard,
    task: taskInput || undefined,
  });
}

interface RunOptions {
  symbol?: string;
  maxTokens?: number;
  maxDepth?: number;
  commentMode?: string;
  copyToClipboard?: boolean;
  saveToFile?: boolean;
  task?: string;
  gitScope?: GitScopeType;
  includeGitDiff?: boolean;
  workspacePath?: string;
  compareBranch?: string;
  commitCount?: number;
}

async function runGeneration(filePath: string, options: RunOptions) {
  await vscode.window.withProgress(
    {
      location: vscode.ProgressLocation.Notification,
      title: 'AI Prompt Context Builder',
      cancellable: true,
    },
    async (progress, token) => {
      let currentPercent = 5;
      progress.report({ message: '(5%) Initializing analyzers...', increment: 5 });

      // Smooth background ticker to keep progress active between phases
      const ticker = setInterval(() => {
        if (currentPercent < 92) {
          currentPercent += 2;
          progress.report({ increment: 2 });
        }
      }, 600);

      const args = ['generate', '--format', 'json'];
      if (options.gitScope === 'all') {
        args.push('--git');
        if (options.workspacePath) args.push('--workspace', options.workspacePath);
      } else if (options.gitScope === 'staged') {
        args.push('--git-staged');
        if (options.workspacePath) args.push('--workspace', options.workspacePath);
      } else if (options.gitScope === 'unstaged') {
        args.push('--git-unstaged');
        if (options.workspacePath) args.push('--workspace', options.workspacePath);
      } else if (options.gitScope === 'branch') {
        args.push('--git-branch', options.compareBranch || 'main');
        if (options.workspacePath) args.push('--workspace', options.workspacePath);
      } else if (options.gitScope === 'commits') {
        args.push('--git-commits', (options.commitCount || 1).toString());
        if (options.workspacePath) args.push('--workspace', options.workspacePath);
      } else if (options.gitScope === 'file') {
        args.push('--file', filePath, '--git-file');
      } else {
        args.push('--file', filePath);
      }

      if (options.gitScope && options.gitScope !== 'none' && options.includeGitDiff === false) {
        args.push('--no-diff');
      }
      if (options.symbol) args.push('--symbol', options.symbol);
      if (options.maxTokens) args.push('--max-tokens', options.maxTokens.toString());
      if (options.maxDepth) args.push('--max-depth', options.maxDepth.toString());
      if (options.commentMode) args.push('--comments', options.commentMode.toLowerCase());
      if (options.task) args.push('--task', options.task);

      const spec = resolveCommand(args);
      outputChannel.appendLine(`[Execute] ${spec.command} ${spec.args.join(' ')} (resolved via ${spec.description})`);

      const child = spawn(spec.command, spec.args, spec.options);

      token.onCancellationRequested(() => {
        child.kill();
        clearInterval(ticker);
        outputChannel.appendLine('Prompt generation cancelled by user.');
      });

      let stdout = '';
      let stderr = '';

      child.stdout.on('data', (d) => { stdout += d.toString(); });
      child.stderr.on('data', (d) => {
        const text = d.toString();
        stderr += text;

        // Parse PROGRESS: XX% - Message
        const progressRegex = /PROGRESS:\s*(\d+)%\s*-\s*([^\r\n]+)/g;
        let match;
        while ((match = progressRegex.exec(text)) !== null) {
          const targetPercent = parseInt(match[1], 10);
          const phaseName = match[2].trim();
          const increment = Math.max(0, targetPercent - currentPercent);
          currentPercent = targetPercent;
          progress.report({ message: `(${targetPercent}%) ${phaseName}`, increment });
        }
      });

      await new Promise<void>((resolve, reject) => {
        child.on('error', async (err) => {
          clearInterval(ticker);
          outputChannel.appendLine(`Failed to launch process '${spec.command}': ${err.message}`);
          await handleMissingRuntime(spec.command, err);
          reject(err);
        });

        child.on('close', (code) => {
          clearInterval(ticker);
          progress.report({ message: '(100%) Complete!', increment: Math.max(0, 100 - currentPercent) });

          if (code === 0) {
            try {
              const res = JSON.parse(stdout);
              if (res.success) {
                lastGeneratedPath = res.outputPath;
                if (res.outputPath && historyTreeProvider) {
                  historyTreeProvider.addGeneratedFile(res.outputPath, res.filesIncluded, res.finalEstimatedTokens);
                }
                outputChannel.appendLine(`Generated: ${res.outputPath} (${res.filesIncluded} files, ~${res.finalEstimatedTokens} tokens)`);
                handleSuccess(res);
                resolve();
              } else {
                vscode.window.showErrorMessage(`Prompt Generation Failed: ${res.message || 'Unknown error'}`);
                reject(new Error(res.message));
              }
            } catch (e: any) {
              vscode.window.showErrorMessage(`Failed to parse result from generator: ${e.message}`);
              reject(e);
            }
          } else {
            vscode.window.showErrorMessage(`Process exited with code ${code}. Error: ${stderr || stdout}`);
            reject(new Error(`Exit code ${code}`));
          }
        });
      });
    }
  );
}

function handleSuccess(res: any) {
  const actions = ['Open Markdown'];
  if (res.outputPath) actions.push('Reveal in Explorer');
  actions.push('Copy Path');

  vscode.window
    .showInformationMessage(
      `Context generated: ${res.filesIncluded} files (~${res.finalEstimatedTokens.toLocaleString()} tokens).`,
      ...actions
    )
    .then(async (action) => {
      if (action === 'Open Markdown' && res.outputPath) {
        const doc = await vscode.workspace.openTextDocument(res.outputPath);
        await vscode.window.showTextDocument(doc);
      } else if (action === 'Reveal in Explorer' && res.outputPath) {
        await vscode.commands.executeCommand('revealFileInOS', vscode.Uri.file(res.outputPath));
      } else if (action === 'Copy Path' && res.outputPath) {
        await vscode.env.clipboard.writeText(res.outputPath);
      }
    });
}

async function installDotnetViaExtension(): Promise<string | undefined> {
  return await vscode.window.withProgress(
    {
      location: vscode.ProgressLocation.Notification,
      title: 'Acquiring .NET Runtime for AI Context Builder...',
      cancellable: false,
    },
    async (progress) => {
      try {
        progress.report({ message: 'Checking VS Code .NET Install Tool...' });
        const dotnetExt = vscode.extensions.getExtension('ms-dotnettools.vscode-dotnet-runtime');
        if (!dotnetExt) {
          progress.report({ message: 'Installing VS Code .NET Install Tool...' });
          await vscode.commands.executeCommand('workbench.extensions.installExtension', 'ms-dotnettools.vscode-dotnet-runtime');
        }

        progress.report({ message: 'Downloading portable .NET runtime...' });
        const res = await vscode.commands.executeCommand<{ dotnetPath: string }>('dotnet.acquire', {
          version: '10.0',
          requestingExtensionId: 'sitholewb.ai-prompt-context-builder',
        });

        if (res && res.dotnetPath) {
          if (extContext) {
            await extContext.globalState.update('acquiredDotnetPath', res.dotnetPath);
          }
          vscode.window.showInformationMessage(`Portable .NET runtime acquired successfully: ${res.dotnetPath}`);
          outputChannel.appendLine(`[Runtime] Acquired portable runtime at: ${res.dotnetPath}`);
          return res.dotnetPath;
        }
      } catch (err: any) {
        outputChannel.appendLine(`[Runtime] Auto-install via .NET Install Tool failed: ${err.message}`);
        vscode.window.showWarningMessage(
          `Could not auto-acquire .NET runtime via VS Code: ${err.message}. Please install .NET manually.`,
          'Open Download Page'
        ).then((choice) => {
          if (choice === 'Open Download Page') {
            vscode.env.openExternal(vscode.Uri.parse('https://dotnet.microsoft.com/download/dotnet/10.0'));
          }
        });
      }
      return undefined;
    }
  );
}

async function handleMissingRuntime(failedCommand: string, error?: Error): Promise<void> {
  const isWindows = process.platform === 'win32';
  const isMac = process.platform === 'darwin';

  outputChannel.appendLine(`[Runtime Missing] Command '${failedCommand}' failed: ${error?.message || 'Not found'}`);

  const autoInstallAction = 'Auto-Install via VS Code (.NET Tool)';
  const terminalAction = isWindows ? 'Install via Winget (Terminal)' : (isMac ? 'Install via Homebrew' : 'Install via Package Manager');
  const downloadAction = 'Download .NET (.exe)';
  const configureAction = 'Configure Executable Path';

  const selected = await vscode.window.showErrorMessage(
    `AI Context Builder requires the .NET runtime to generate prompts and analyze dependencies, but '${failedCommand}' was not found.`,
    autoInstallAction,
    terminalAction,
    downloadAction,
    configureAction
  );

  if (selected === autoInstallAction) {
    await installDotnetViaExtension();
  } else if (selected === terminalAction) {
    const term = vscode.window.createTerminal('Install .NET Runtime');
    term.show();
    if (isWindows) {
      term.sendText('winget install Microsoft.DotNet.Runtime.10 || winget install Microsoft.DotNet.DesktopRuntime.10');
    } else if (isMac) {
      term.sendText('brew install dotnet');
    } else {
      term.sendText('sudo apt-get update && sudo apt-get install -y dotnet-runtime-10.0 || sudo dnf install dotnet-runtime-10.0');
    }
  } else if (selected === downloadAction) {
    vscode.env.openExternal(vscode.Uri.parse('https://dotnet.microsoft.com/download/dotnet/10.0'));
  } else if (selected === configureAction) {
    vscode.commands.executeCommand('workbench.action.openSettings', 'aiPromptContextBuilder.executablePath');
  }
}

export function deactivate() {}

class ActionTreeItem extends vscode.TreeItem {
  constructor(
    label: string,
    description: string,
    tooltip: string,
    iconName: string,
    commandId: string,
    args: any[] = []
  ) {
    super(label, vscode.TreeItemCollapsibleState.None);
    this.description = description;
    this.tooltip = tooltip;
    this.iconPath = new vscode.ThemeIcon(iconName);
    this.command = {
      command: commandId,
      title: label,
      arguments: args,
    };
  }
}

class ActionsTreeProvider implements vscode.TreeDataProvider<ActionTreeItem> {
  getTreeItem(element: ActionTreeItem): vscode.TreeItem {
    return element;
  }

  getChildren(): vscode.ProviderResult<ActionTreeItem[]> {
    return [
      new ActionTreeItem(
        'Generate AI Prompt & Context...',
        'Interactive Popup',
        'Open the full interactive popup selector (Dependencies Only vs Git Scopes, Diff Options & Export Mode)',
        'sparkle',
        'aiPromptContextBuilder.generateFromFile'
      ),
      new ActionTreeItem(
        'All Git Changes + Dependencies',
        'Staged + Unstaged',
        'Select All Git Changes and open popup for Diff & Export options',
        'git-merge',
        'aiPromptContextBuilder.generateWithScopePopup',
        ['all']
      ),
      new ActionTreeItem(
        'Staged Git Changes ONLY',
        'git diff --cached',
        'Select Staged Git Changes ONLY and open popup for Diff & Export options',
        'diff-added',
        'aiPromptContextBuilder.generateWithScopePopup',
        ['staged']
      ),
      new ActionTreeItem(
        'Unstaged Git Changes ONLY',
        'git diff',
        'Select Unstaged Git Changes ONLY and open popup for Diff & Export options',
        'diff-modified',
        'aiPromptContextBuilder.generateWithScopePopup',
        ['unstaged']
      ),
      new ActionTreeItem(
        'Compare Branch / Pull Request...',
        'PR / Branch Diff',
        'Compare current branch against main/develop/origin and resolve all PR files + dependency trees',
        'git-pull-request',
        'aiPromptContextBuilder.generateWithScopePopup',
        ['branch']
      ),
      new ActionTreeItem(
        'Last X Commits + Dependencies...',
        'Recent Commits',
        'Select last 1, 2, 3, 5, or N commits and resolve all modified files + dependency trees',
        'git-commit',
        'aiPromptContextBuilder.generateWithScopePopup',
        ['commits']
      ),
      new ActionTreeItem(
        'Active File: Dependencies Only',
        'No Git Diff',
        'Trace dependency tree of the active editor file (opens Export Mode popup)',
        'file-code',
        'aiPromptContextBuilder.generateWithScopePopup',
        ['none']
      ),
      new ActionTreeItem(
        'Active File: Git Diff + Dependencies',
        'Single File',
        'Trace dependency tree of the active editor file and include its Git diff',
        'diff',
        'aiPromptContextBuilder.generateWithScopePopup',
        ['file']
      ),
      new ActionTreeItem(
        'Symbol at Cursor + Dependencies',
        'Focused Root',
        'Generate context rooted at the symbol under the cursor',
        'symbol-method',
        'aiPromptContextBuilder.generateFromSymbol'
      ),
      new ActionTreeItem(
        'Check Language Workers',
        'Diagnostics',
        'Verify compiler & analyzer status across .NET, TypeScript, Python, Rust, Go, Java, C++, PHP, Dart & Kotlin',
        'check-all',
        'aiPromptContextBuilder.checkWorkers'
      ),
    ];
  }
}

interface HistoryEntry {
  filePath: string;
  filesIncluded: number;
  tokens: number;
  timestamp: number;
}

class HistoryTreeProvider implements vscode.TreeDataProvider<vscode.TreeItem> {
  private _onDidChangeTreeData = new vscode.EventEmitter<vscode.TreeItem | undefined | void>();
  readonly onDidChangeTreeData = this._onDidChangeTreeData.event;
  private entries: HistoryEntry[] = [];

  constructor(private context: vscode.ExtensionContext) {
    this.entries = this.context.workspaceState.get<HistoryEntry[]>('recentGeneratedPrompts', []);
    this.discoverWorkspaceContextFiles();
  }

  private discoverWorkspaceContextFiles() {
    const wsFolder = vscode.workspace.workspaceFolders?.[0]?.uri.fsPath;
    if (!wsFolder || !fs.existsSync(wsFolder)) return;
    try {
      const files = fs.readdirSync(wsFolder);
      for (const f of files) {
        if (f.endsWith('-context.md')) {
          const full = path.join(wsFolder, f);
          if (!this.entries.some((e) => e.filePath === full)) {
            const stat = fs.statSync(full);
            this.entries.push({
              filePath: full,
              filesIncluded: 0,
              tokens: 0,
              timestamp: stat.mtimeMs,
            });
          }
        }
      }
      this.entries.sort((a, b) => b.timestamp - a.timestamp);
    } catch {
      // ignore read errors
    }
  }

  addGeneratedFile(filePath: string, filesIncluded: number, tokens: number) {
    this.entries = this.entries.filter((e) => e.filePath !== filePath);
    this.entries.unshift({
      filePath,
      filesIncluded,
      tokens,
      timestamp: Date.now(),
    });
    if (this.entries.length > 15) {
      this.entries = this.entries.slice(0, 15);
    }
    this.context.workspaceState.update('recentGeneratedPrompts', this.entries);
    this._onDidChangeTreeData.fire();
  }

  refresh() {
    this.entries = this.entries.filter((e) => fs.existsSync(e.filePath));
    this.context.workspaceState.update('recentGeneratedPrompts', this.entries);
    this._onDidChangeTreeData.fire();
  }

  getTreeItem(element: vscode.TreeItem): vscode.TreeItem {
    return element;
  }

  getChildren(): vscode.ProviderResult<vscode.TreeItem[]> {
    const valid = this.entries.filter((e) => fs.existsSync(e.filePath));
    if (valid.length === 0) {
      const emptyItem = new vscode.TreeItem('No generated context files yet', vscode.TreeItemCollapsibleState.None);
      emptyItem.description = 'Right-click a file or use actions above';
      emptyItem.iconPath = new vscode.ThemeIcon('info');
      return [emptyItem];
    }

    return valid.map((entry) => {
      const fileName = path.basename(entry.filePath);
      const item = new vscode.TreeItem(fileName, vscode.TreeItemCollapsibleState.None);
      item.description =
        entry.tokens > 0
          ? `${entry.filesIncluded} files • ~${entry.tokens.toLocaleString()} tokens`
          : 'Click to open';
      item.tooltip = entry.filePath;
      item.iconPath = new vscode.ThemeIcon('markdown');
      item.command = {
        command: 'aiPromptContextBuilder.openHistoryFile',
        title: 'Open Generated Markdown',
        arguments: [entry.filePath],
      };
      return item;
    });
  }
}
