import * as vscode from 'vscode';
import { spawn } from 'child_process';
import * as path from 'path';
import * as fs from 'fs';

let lastGeneratedPath: string | null = null;
let outputChannel: vscode.OutputChannel;
let extContext: vscode.ExtensionContext;

export function activate(context: vscode.ExtensionContext) {
  extContext = context;
  outputChannel = vscode.window.createOutputChannel('AI Context & Prompt Builder');
  context.subscriptions.push(outputChannel);

  const registerCmd = (name: string, handler: (...args: any[]) => any) => {
    context.subscriptions.push(vscode.commands.registerCommand(`aiPromptContextBuilder.${name}`, handler));
    // Register backwards-compatible alias
    context.subscriptions.push(vscode.commands.registerCommand(`aiContextBuilder.${name}`, handler));
  };

  // 1. Generate from Current File
  registerCmd('generateFromFile', async (uri?: vscode.Uri) => {
    const filePath = uri?.fsPath || vscode.window.activeTextEditor?.document.uri.fsPath;
    if (!filePath) {
      vscode.window.showErrorMessage('No active file selected to generate AI context.');
      return;
    }
    await runGeneration(filePath, {});
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

    // Prompt for Token Budget
    const budgetPick = await vscode.window.showQuickPick(
      [
        { label: 'Unlimited', description: 'Include all eligible transitive dependencies' },
        { label: '32,000', description: '32k token budget' },
        { label: '64,000', description: '64k token budget' },
        { label: '100,000', description: '100k token budget' },
        { label: '128,000', description: '128k token budget' },
        { label: '200,000', description: '200k token budget' },
        { label: 'Custom', description: 'Enter specific integer token budget' },
      ],
      { title: 'Select Token Budget' }
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

    // Prompt for Comment Mode
    const commentPick = await vscode.window.showQuickPick(
      [
        { label: 'Preserve', description: 'Retain original comments' },
        { label: 'Auto', description: 'Remove comments only if token budget is exceeded' },
        { label: 'Remove', description: 'Remove comments' },
      ],
      { title: 'Select Comment Mode' }
    );
    if (!commentPick) return;

    // Prompt for Max Depth
    const depthPick = await vscode.window.showQuickPick(
      [
        { label: 'Unlimited', description: 'Traverse full transitive dependency tree' },
        { label: '1', description: 'Direct dependencies only' },
        { label: '2', description: 'Depth 2' },
        { label: '3', description: 'Depth 3' },
        { label: '5', description: 'Depth 5' },
        { label: 'Custom', description: 'Enter custom integer depth' },
      ],
      { title: 'Select Maximum Traversal Depth' }
    );
    if (!depthPick) return;

    let maxDepth: number | undefined;
    if (depthPick.label === 'Custom') {
      const val = await vscode.window.showInputBox({ prompt: 'Enter max traversal depth (integer)' });
      if (val && parseInt(val, 10)) maxDepth = parseInt(val, 10);
    } else if (depthPick.label !== 'Unlimited') {
      maxDepth = parseInt(depthPick.label, 10);
    }

    // Prompt for Optional Task
    const taskInput = await vscode.window.showInputBox({
      prompt: 'Optional: Enter custom refactoring or analysis task for the Markdown prompt',
      placeHolder: 'e.g. Refactor service to use asynchronous repository calls'
    });

    await runGeneration(filePath, {
      maxTokens,
      commentMode: commentPick.label,
      maxDepth,
      task: taskInput,
    });
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

interface RunOptions {
  symbol?: string;
  maxTokens?: number;
  maxDepth?: number;
  commentMode?: string;
  copyToClipboard?: boolean;
  saveToFile?: boolean;
  task?: string;
}

async function runGeneration(filePath: string, options: RunOptions) {
  const phases = [
    'Detecting workspace',
    'Detecting language',
    'Starting analyser',
    'Loading project',
    'Resolving root',
    'Building dependency graph',
    'Enhancing framework context',
    'Reading source files',
    'Applying comment policy',
    'Applying token budget',
    'Writing Markdown',
  ];

  await vscode.window.withProgress(
    {
      location: vscode.ProgressLocation.Notification,
      title: 'Generating AI Prompt & Context...',
      cancellable: true,
    },
    async (progress, token) => {
      let currentPhase = 0;
      const interval = setInterval(() => {
        if (currentPhase < phases.length) {
          progress.report({ message: phases[currentPhase], increment: 100 / phases.length });
          currentPhase++;
        }
      }, 250);

      const args = ['generate', '--file', filePath, '--format', 'json'];
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
        clearInterval(interval);
        outputChannel.appendLine('Prompt generation cancelled by user.');
      });

      let stdout = '';
      let stderr = '';

      child.stdout.on('data', (d) => { stdout += d.toString(); });
      child.stderr.on('data', (d) => { stderr += d.toString(); });

      await new Promise<void>((resolve, reject) => {
        child.on('error', async (err) => {
          clearInterval(interval);
          outputChannel.appendLine(`Failed to launch process '${spec.command}': ${err.message}`);
          await handleMissingRuntime(spec.command, err);
          reject(err);
        });

        child.on('close', (code) => {
          clearInterval(interval);
          if (code === 0) {
            try {
              const res = JSON.parse(stdout);
              if (res.success) {
                lastGeneratedPath = res.outputPath;
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
