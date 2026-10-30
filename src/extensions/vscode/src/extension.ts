import * as vscode from 'vscode';
import { spawn, execSync } from 'child_process';
import * as path from 'path';
import * as fs from 'fs';
import * as http from 'http';

type GitScopeType = 'none' | 'all' | 'staged' | 'unstaged' | 'file' | 'branch' | 'commits' | 'draftCommit';
type SupportedLanguageCode = 'en' | 'es' | 'fr' | 'de' | 'zh' | 'ja' | 'pt' | 'ru' | 'zu' | 'af';

interface LanguageOption {
  code: SupportedLanguageCode;
  badge: string;
  flag: string;
  nativeName: string;
  englishName: string;
  regionSubtitle: string;
}

const LANGUAGE_CATALOG: LanguageOption[] = [
  {
    code: 'en',
    badge: 'EN',
    flag: '🇺🇸',
    nativeName: 'English (US)',
    englishName: 'English',
    regionSubtitle: 'English • United States / Global',
  },
  {
    code: 'es',
    badge: 'ES',
    flag: '🇪🇸',
    nativeName: 'Español',
    englishName: 'Spanish',
    regionSubtitle: 'Spanish • España / Latinoamérica',
  },
  {
    code: 'fr',
    badge: 'FR',
    flag: '🇫🇷',
    nativeName: 'Français',
    englishName: 'French',
    regionSubtitle: 'French • France / Europe',
  },
  {
    code: 'de',
    badge: 'DE',
    flag: '🇩🇪',
    nativeName: 'Deutsch',
    englishName: 'German',
    regionSubtitle: 'German • Deutschland / DACH',
  },
  {
    code: 'zh',
    badge: 'ZH',
    flag: '🇨🇳',
    nativeName: '简体中文',
    englishName: 'Chinese',
    regionSubtitle: 'Chinese • Asia / Global',
  },
  {
    code: 'ja',
    badge: 'JA',
    flag: '🇯🇵',
    nativeName: '日本語',
    englishName: 'Japanese',
    regionSubtitle: 'Japanese • Japan',
  },
  {
    code: 'pt',
    badge: 'PT',
    flag: '🇧🇷',
    nativeName: 'Português',
    englishName: 'Portuguese',
    regionSubtitle: 'Portuguese • Brasil / Portugal',
  },
  {
    code: 'ru',
    badge: 'RU',
    flag: '🇷🇺',
    nativeName: 'Русский',
    englishName: 'Russian',
    regionSubtitle: 'Russian • Eastern Europe / CIS',
  },
  {
    code: 'zu',
    badge: 'ZU',
    flag: '🇿🇦',
    nativeName: 'isiZulu',
    englishName: 'Zulu',
    regionSubtitle: 'Zulu • South Africa / SADC',
  },
  {
    code: 'af',
    badge: 'AF',
    flag: '🇿🇦',
    nativeName: 'Afrikaans',
    englishName: 'Afrikaans',
    regionSubtitle: 'Afrikaans • Suid-Afrika / Namibia',
  },
];

interface LocalizedUiStrings {
  generatePrompt: string;
  interactivePopup: string;
  draftCommit: string;
  noAiBuiltIn: string;
  allGitChanges: string;
  stagedUnstaged: string;
  stagedOnly: string;
  unstagedOnly: string;
  compareBranch: string;
  lastXCommits: string;
  activeFileDeps: string;
  activeFileGit: string;
  symbolAtCursor: string;
  checkWorkers: string;
  changeLanguage: string;
  languageChangedMsg: (lang: LanguageOption) => string;
  exportDefaults: string;
  customOptions: string;
  chooseExportMode: string;
}

const UI_STRINGS: Record<SupportedLanguageCode, LocalizedUiStrings> = {
  en: {
    generatePrompt: 'Generate AI Prompt & Context...',
    interactivePopup: 'Interactive Popup',
    draftCommit: 'Draft Git Commit Message...',
    noAiBuiltIn: 'No AI / Built-in LM',
    allGitChanges: 'All Git Changes + Dependencies',
    stagedUnstaged: 'Staged + Unstaged',
    stagedOnly: 'Staged Git Changes ONLY',
    unstagedOnly: 'Unstaged Git Changes ONLY',
    compareBranch: 'Compare Branch / Pull Request...',
    lastXCommits: 'Last X Commits + Dependencies...',
    activeFileDeps: 'Active File: Dependencies Only',
    activeFileGit: 'Active File: Git Diff + Dependencies',
    symbolAtCursor: 'Symbol at Cursor + Dependencies',
    checkWorkers: 'Check Language Workers',
    changeLanguage: 'Change Language',
    languageChangedMsg: (l) => `Language set to ${l.flag} ${l.nativeName} (${l.badge}). Generated AI prompts and UI labels will now use ${l.nativeName}.`,
    exportDefaults: '$(zap) Export with Defaults',
    customOptions: '$(gear) Custom Options...',
    chooseExportMode: 'Choose Export Mode',
  },
  es: {
    generatePrompt: 'Generar Prompt y Contexto IA...',
    interactivePopup: 'Menú Interactivo',
    draftCommit: 'Redactar Mensaje de Commit Git...',
    noAiBuiltIn: 'Sin IA / Modelo Integrado',
    allGitChanges: 'Todos los Cambios Git + Dependencias',
    stagedUnstaged: 'Staged + Unstaged',
    stagedOnly: 'SOLO Cambios Git Staged',
    unstagedOnly: 'SOLO Cambios Git Unstaged',
    compareBranch: 'Comparar Rama / Pull Request...',
    lastXCommits: 'Últimos X Commits + Dependencias...',
    activeFileDeps: 'Archivo Activo: Solo Dependencias',
    activeFileGit: 'Archivo Activo: Diff Git + Dependencias',
    symbolAtCursor: 'Símbolo en Cursor + Dependencias',
    checkWorkers: 'Verificar Analizadores de Lenguaje',
    changeLanguage: 'Cambiar Idioma (Change Language)',
    languageChangedMsg: (l) => `Idioma cambiado a ${l.flag} ${l.nativeName} (${l.badge}). Los prompts de IA y el menú ahora usarán ${l.nativeName}.`,
    exportDefaults: '$(zap) Exportar con Valores Predeterminados',
    customOptions: '$(gear) Opciones Personalizadas...',
    chooseExportMode: 'Elegir Modo de Exportación',
  },
  fr: {
    generatePrompt: 'Générer Prompt & Contexte IA...',
    interactivePopup: 'Menu Interactif',
    draftCommit: 'Rédiger un Message de Commit Git...',
    noAiBuiltIn: 'Sans IA / Modèle Intégré',
    allGitChanges: 'Tous les Changements Git + Dépendances',
    stagedUnstaged: 'Staged + Unstaged',
    stagedOnly: 'Changements Git Staged UNIQUEMENT',
    unstagedOnly: 'Changements Git Unstaged UNIQUEMENT',
    compareBranch: 'Comparer Branche / Pull Request...',
    lastXCommits: 'X Derniers Commits + Dépendances...',
    activeFileDeps: 'Fichier Actif : Dépendances Seules',
    activeFileGit: 'Fichier Actif : Diff Git + Dépendances',
    symbolAtCursor: 'Symbole sous le Curseur + Dépendances',
    checkWorkers: 'Vérifier les Analyseurs de Langage',
    changeLanguage: 'Changer de Langue (Change Language)',
    languageChangedMsg: (l) => `Langue définie sur ${l.flag} ${l.nativeName} (${l.badge}). Les prompts IA utiliseront désormais ${l.nativeName}.`,
    exportDefaults: '$(zap) Exporter par Défaut',
    customOptions: '$(gear) Options Personnalisées...',
    chooseExportMode: "Choisir le Mode d'Exportation",
  },
  de: {
    generatePrompt: 'KI-Prompt & Kontext generieren...',
    interactivePopup: 'Interaktives Menü',
    draftCommit: 'Git-Commit-Nachricht entwerfen...',
    noAiBuiltIn: 'Ohne KI / Integriertes Modell',
    allGitChanges: 'Alle Git-Änderungen + Abhängigkeiten',
    stagedUnstaged: 'Staged + Unstaged',
    stagedOnly: 'NUR Staged Git-Änderungen',
    unstagedOnly: 'NUR Unstaged Git-Änderungen',
    compareBranch: 'Branch / Pull Request vergleichen...',
    lastXCommits: 'Letzte X Commits + Abhängigkeiten...',
    activeFileDeps: 'Aktive Datei: Nur Abhängigkeiten',
    activeFileGit: 'Aktive Datei: Git-Diff + Abhängigkeiten',
    symbolAtCursor: 'Symbol am Cursor + Abhängigkeiten',
    checkWorkers: 'Sprach-Analysatoren prüfen',
    changeLanguage: 'Sprache ändern (Change Language)',
    languageChangedMsg: (l) => `Sprache auf ${l.flag} ${l.nativeName} (${l.badge}) eingestellt.`,
    exportDefaults: '$(zap) Mit Standardwerten exportieren',
    customOptions: '$(gear) Benutzerdefinierte Optionen...',
    chooseExportMode: 'Exportmodus wählen',
  },
  zh: {
    generatePrompt: '生成 AI 提示词与代码上下文...',
    interactivePopup: '交互式菜单',
    draftCommit: '起草 Git 提交信息 (Commit)...',
    noAiBuiltIn: '无 AI 规则 / 内置模型',
    allGitChanges: '所有 Git 更改 + 依赖树',
    stagedUnstaged: '暂存 + 未暂存',
    stagedOnly: '仅已暂存 (Staged) Git 更改',
    unstagedOnly: '仅未暂存 (Unstaged) Git 更改',
    compareBranch: '比较分支 / Pull Request...',
    lastXCommits: '最近 X 次提交 + 依赖树...',
    activeFileDeps: '当前文件：仅依赖树',
    activeFileGit: '当前文件：Git Diff + 依赖树',
    symbolAtCursor: '光标处符号 + 依赖树',
    checkWorkers: '检查语言分析引擎',
    changeLanguage: '切换语言 (Change Language)',
    languageChangedMsg: (l) => `语言已切换为 ${l.flag} ${l.nativeName} (${l.badge})。生成的 AI 提示词将要求使用${l.nativeName}回复。`,
    exportDefaults: '$(zap) 使用默认设置快速导出',
    customOptions: '$(gear) 自定义选项...',
    chooseExportMode: '选择导出模式',
  },
  ja: {
    generatePrompt: 'AIプロンプトとコンテキストを生成...',
    interactivePopup: '対話型ポップアップ',
    draftCommit: 'Gitコミットメッセージを作成...',
    noAiBuiltIn: 'AIなし / 内蔵モデル',
    allGitChanges: 'すべてのGit変更 + 依存関係ツリー',
    stagedUnstaged: 'ステージ済 + 未ステージ',
    stagedOnly: 'ステージ済 (Staged) Git変更のみ',
    unstagedOnly: '未ステージ (Unstaged) Git変更のみ',
    compareBranch: 'ブランチ / PR を比較...',
    lastXCommits: '直近 X 件のコミット + 依存関係...',
    activeFileDeps: '現在のファイル: 依存関係のみ',
    activeFileGit: '現在のファイル: Git Diff + 依存関係',
    symbolAtCursor: 'カーソル位置のシンボル + 依存関係',
    checkWorkers: '言語アナライザーを確認',
    changeLanguage: '言語を変更 (Change Language)',
    languageChangedMsg: (l) => `言語を ${l.flag} ${l.nativeName} (${l.badge}) に設定しました。`,
    exportDefaults: '$(zap) デフォルト設定でエクスポート',
    customOptions: '$(gear) カスタムオプション...',
    chooseExportMode: 'エクスポートモードを選択',
  },
  pt: {
    generatePrompt: 'Gerar Prompt e Contexto de IA...',
    interactivePopup: 'Menu Interativo',
    draftCommit: 'Redigir Mensagem de Commit Git...',
    noAiBuiltIn: 'Sem IA / Modelo Integrado',
    allGitChanges: 'Todas as Alterações Git + Dependências',
    stagedUnstaged: 'Staged + Unstaged',
    stagedOnly: 'APENAS Alterações Git Staged',
    unstagedOnly: 'APENAS Alterações Git Unstaged',
    compareBranch: 'Comparar Branch / Pull Request...',
    lastXCommits: 'Últimos X Commits + Dependências...',
    activeFileDeps: 'Arquivo Ativo: Apenas Dependências',
    activeFileGit: 'Arquivo Ativo: Diff Git + Dependências',
    symbolAtCursor: 'Símbolo no Cursor + Dependências',
    checkWorkers: 'Verificar Analisadores de Linguagem',
    changeLanguage: 'Alterar Idioma (Change Language)',
    languageChangedMsg: (l) => `Idioma alterado para ${l.flag} ${l.nativeName} (${l.badge}).`,
    exportDefaults: '$(zap) Exportar com Padrões',
    customOptions: '$(gear) Opções Personalizadas...',
    chooseExportMode: 'Escolher Modo de Exportação',
  },
  ru: {
    generatePrompt: 'Создать AI-промпт и контекст...',
    interactivePopup: 'Интерактивное меню',
    draftCommit: 'Создать сообщение Git Commit...',
    noAiBuiltIn: 'Без ИИ / Встроенная модель',
    allGitChanges: 'Все изменения Git + Зависимости',
    stagedUnstaged: 'Staged + Unstaged',
    stagedOnly: 'ТОЛЬКО Staged изменения Git',
    unstagedOnly: 'ТОЛЬКО Unstaged изменения Git',
    compareBranch: 'Сравнить ветку / Pull Request...',
    lastXCommits: 'Последние X коммитов + Зависимости...',
    activeFileDeps: 'Активный файл: Только зависимости',
    activeFileGit: 'Активный файл: Git Diff + Зависимости',
    symbolAtCursor: 'Символ под курсором + Зависимости',
    checkWorkers: 'Проверить языковые анализаторы',
    changeLanguage: 'Изменить язык (Change Language)',
    languageChangedMsg: (l) => `Язык изменен на ${l.flag} ${l.nativeName} (${l.badge}).`,
    exportDefaults: '$(zap) Экспорт по умолчанию',
    customOptions: '$(gear) Пользовательские настройки...',
    chooseExportMode: 'Выберите режим экспорта',
  },
  zu: {
    generatePrompt: 'Khiqiza i-AI Prompt kanye ne-Context...',
    interactivePopup: 'Imenyu Yokukhetha',
    draftCommit: 'Bhala Umlayezo we-Git Commit...',
    noAiBuiltIn: 'Ngaphandle kwe-AI / Imodeli Yangaphakathi',
    allGitChanges: 'Zonke Izinguquko ze-Git + ama-Dependencies',
    stagedUnstaged: 'Staged + Unstaged',
    stagedOnly: 'Izinguquko ze-Git ezi-Staged KUPHELA',
    unstagedOnly: 'Izinguquko ze-Git ezi-Unstaged KUPHELA',
    compareBranch: 'Qhathanisa i-Branch / Pull Request...',
    lastXCommits: 'Ama-Commits Okugcina angu-X + Dependencies...',
    activeFileDeps: 'Ifayela Elivuliwe: Ama-Dependencies Kuphela',
    activeFileGit: 'Ifayela Elivuliwe: Git Diff + Dependencies',
    symbolAtCursor: 'Igama elikwi-Cursor + Dependencies',
    checkWorkers: 'Hlola Izinjini Zokuhlaziya Izilimi',
    changeLanguage: 'Shintsha Ulimi (Change Language)',
    languageChangedMsg: (l) => `Ulimi lushintshiwe lwaba yi-${l.flag} ${l.nativeName} (${l.badge}). Ama-AI Prompts azophuma ngesiZulu.`,
    exportDefaults: '$(zap) Khiqiza Ngokushesha (Defaults)',
    customOptions: '$(gear) Izinketho Ezikhethekile (Custom Options)...',
    chooseExportMode: 'Khetha Indlela Yokukhiqiza',
  },
  af: {
    generatePrompt: 'Genereer KI-Prompt & Konteks...',
    interactivePopup: 'Interaktiewe Kieslys',
    draftCommit: 'Skryf Git-Commit-Boodskap...',
    noAiBuiltIn: 'Geen KI / Ingeboude Model',
    allGitChanges: 'Alle Git-Veranderinge + Afhanklikhede',
    stagedUnstaged: 'Staged + Unstaged',
    stagedOnly: 'SLEGS Staged Git-Veranderinge',
    unstagedOnly: 'SLEGS Unstaged Git-Veranderinge',
    compareBranch: 'Vergelyk Tak / Pull Request...',
    lastXCommits: 'Laaste X Commits + Afhanklikhede...',
    activeFileDeps: 'Aktiewe Lêer: Slegs Afhanklikhede',
    activeFileGit: 'Aktiewe Lêer: Git-Diff + Afhanklikhede',
    symbolAtCursor: 'Simbool by Wyser + Afhanklikhede',
    checkWorkers: 'Kontroleer Taalanaliseerders',
    changeLanguage: 'Verander Taal (Change Language)',
    languageChangedMsg: (l) => `Taal gestel op ${l.flag} ${l.nativeName} (${l.badge}).`,
    exportDefaults: '$(zap) Voer uit met Verstekwaardes',
    customOptions: '$(gear) Pasgemaakte Opsies...',
    chooseExportMode: 'Kies Uitvoermodus',
  },
};

function getActiveLanguageCode(): SupportedLanguageCode {
  const fromState = extContext?.globalState.get<string>('outputLanguage');
  const fromConfig = vscode.workspace.getConfiguration('aiPromptContextBuilder').get<string>('outputLanguage');
  const raw = (fromState || fromConfig || 'en').toLowerCase();
  const match = LANGUAGE_CATALOG.find((l) => l.code === raw);
  return match ? match.code : 'en';
}

function getActiveLanguageOption(): LanguageOption {
  const code = getActiveLanguageCode();
  return LANGUAGE_CATALOG.find((l) => l.code === code) || LANGUAGE_CATALOG[0];
}

function getUiStrings(): LocalizedUiStrings {
  return UI_STRINGS[getActiveLanguageCode()] || UI_STRINGS.en;
}

let lastGeneratedPath: string | null = null;
let outputChannel: vscode.OutputChannel;
let extContext: vscode.ExtensionContext;
let actionsTreeProvider: ActionsTreeProvider;
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
  actionsTreeProvider = new ActionsTreeProvider();
  historyTreeProvider = new HistoryTreeProvider(context);
  context.subscriptions.push(
    vscode.window.registerTreeDataProvider('aiPromptContextBuilder.actionsView', actionsTreeProvider),
    vscode.window.registerTreeDataProvider('aiPromptContextBuilder.historyView', historyTreeProvider)
  );

  const registerCmd = (name: string, handler: (...args: any[]) => any) => {
    context.subscriptions.push(vscode.commands.registerCommand(`aiPromptContextBuilder.${name}`, handler));
    // Register backwards-compatible alias
    context.subscriptions.push(vscode.commands.registerCommand(`aiContextBuilder.${name}`, handler));
  };

  // 1f. Change Language (Header Globe Icon + Bottom of Sidebar + Command Palette)
  registerCmd('changeLanguage', async () => {
    await promptAndChangeLanguage();
  });

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

  // 1e. Draft Git Commit Message (AST Rules / VS Code Built-in LM / Local Ollama)
  registerCmd('draftCommitMessage', async (uri?: vscode.Uri) => {
    const filePath = resolveTargetFilePath(uri);
    await promptAndDraftCommitMessage(filePath);
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
    {
      label: '$(comment-discussion) Draft Git Commit Message (Uncommitted Changes)...',
      description: 'AST Rules or Built-in Model',
      detail: 'Draft a Conventional Commit message from uncommitted changes and auto-fill the Git commit box',
      gitScope: 'draftCommit',
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

  if (chosenScope === 'draftCommit') {
    await promptAndDraftCommitMessage(filePath);
    return;
  } else if (chosenScope === 'branch') {
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
  const ui = getUiStrings();
  const activeLang = getActiveLanguageOption();
  const modePick = await vscode.window.showQuickPick(
    [
      {
        label: ui.exportDefaults,
        description: `Instant Generation • ${activeLang.flag} ${activeLang.nativeName}`,
        detail: 'Full dependency graph, automatic token budget, comments preserved, tools/lockfiles excluded',
        isDefaults: true,
      },
      {
        label: ui.customOptions,
        description: 'Step-by-Step Configuration',
        detail: 'Configure token budget, comment stripping, traversal depth, output language, destination & custom prompt',
        isDefaults: false,
      },
    ],
    {
      title: `${ui.chooseExportMode} (${activeLang.flag} ${activeLang.nativeName})`,
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
    outputLanguage: activeLang.code,
  };

  const targetPath = filePath || workspaceFolder || '';

  if (modePick.isDefaults) {
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

  // Step 5: Response / Output Language
  const currentLang = getActiveLanguageOption();
  const langItems = LANGUAGE_CATALOG.map((l) => ({
    label: `${l.flag} ${l.nativeName}`,
    description: l.code === currentLang.code ? `${l.badge} • Current Default` : l.badge,
    detail: l.regionSubtitle,
    code: l.code,
  }));
  // Put current language at the top
  langItems.sort((a, b) => (a.code === currentLang.code ? -1 : b.code === currentLang.code ? 1 : 0));

  const langPick = await vscode.window.showQuickPick(langItems, {
    title: 'Step 5/6: Select AI Prompt Response Language',
    placeHolder: `Current: ${currentLang.flag} ${currentLang.nativeName} (${currentLang.regionSubtitle})`,
  });
  if (!langPick) return;

  // Step 6: Optional Custom Task Prompt
  const taskInput = await vscode.window.showInputBox({
    prompt: 'Step 6/6: (Optional) Enter custom instructions or goal for the AI prompt',
    placeHolder: 'e.g. Refactor this service to use asynchronous patterns, or Add unit tests'
  });

  await runGeneration(filePath, {
    ...baseOptions,
    maxTokens,
    commentMode: commentPick.label,
    maxDepth,
    saveToFile,
    copyToClipboard,
    outputLanguage: langPick.code,
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
  outputLanguage?: SupportedLanguageCode;
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

      const effectiveLang = options.outputLanguage || getActiveLanguageCode();
      if (effectiveLang && effectiveLang !== 'en') {
        args.push('--output-language', effectiveLang);
      }

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
  private _onDidChangeTreeData = new vscode.EventEmitter<ActionTreeItem | undefined | void>();
  readonly onDidChangeTreeData = this._onDidChangeTreeData.event;

  refresh() {
    this._onDidChangeTreeData.fire();
  }

  getTreeItem(element: ActionTreeItem): vscode.TreeItem {
    return element;
  }

  getChildren(): vscode.ProviderResult<ActionTreeItem[]> {
    const ui = getUiStrings();
    const lang = getActiveLanguageOption();

    return [
      new ActionTreeItem(
        ui.generatePrompt,
        ui.interactivePopup,
        'Open the full interactive popup selector (Dependencies Only vs Git Scopes, Diff Options & Export Mode)',
        'sparkle',
        'aiPromptContextBuilder.generateFromFile'
      ),
      new ActionTreeItem(
        ui.draftCommit,
        ui.noAiBuiltIn,
        'Draft a Conventional Commit message from uncommitted changes (AST rules or VS Code built-in model) and fill the Git commit box',
        'comment-discussion',
        'aiPromptContextBuilder.draftCommitMessage'
      ),
      new ActionTreeItem(
        ui.allGitChanges,
        ui.stagedUnstaged,
        'Select All Git Changes and open popup for Diff & Export options',
        'git-merge',
        'aiPromptContextBuilder.generateWithScopePopup',
        ['all']
      ),
      new ActionTreeItem(
        ui.stagedOnly,
        'git diff --cached',
        'Select Staged Git Changes ONLY and open popup for Diff & Export options',
        'diff-added',
        'aiPromptContextBuilder.generateWithScopePopup',
        ['staged']
      ),
      new ActionTreeItem(
        ui.unstagedOnly,
        'git diff',
        'Select Unstaged Git Changes ONLY and open popup for Diff & Export options',
        'diff-modified',
        'aiPromptContextBuilder.generateWithScopePopup',
        ['unstaged']
      ),
      new ActionTreeItem(
        ui.compareBranch,
        'PR / Branch Diff',
        'Compare current branch against main/develop/origin and resolve all PR files + dependency trees',
        'git-pull-request',
        'aiPromptContextBuilder.generateWithScopePopup',
        ['branch']
      ),
      new ActionTreeItem(
        ui.lastXCommits,
        'Recent Commits',
        'Select last 1, 2, 3, 5, or N commits and resolve all modified files + dependency trees',
        'git-commit',
        'aiPromptContextBuilder.generateWithScopePopup',
        ['commits']
      ),
      new ActionTreeItem(
        ui.activeFileDeps,
        'No Git Diff',
        'Trace dependency tree of the active editor file (opens Export Mode popup)',
        'file-code',
        'aiPromptContextBuilder.generateWithScopePopup',
        ['none']
      ),
      new ActionTreeItem(
        ui.activeFileGit,
        'Single File',
        'Trace dependency tree of the active editor file and include its Git diff',
        'diff',
        'aiPromptContextBuilder.generateWithScopePopup',
        ['file']
      ),
      new ActionTreeItem(
        ui.symbolAtCursor,
        'Focused Root',
        'Generate context rooted at the symbol under the cursor',
        'symbol-method',
        'aiPromptContextBuilder.generateFromSymbol'
      ),
      new ActionTreeItem(
        ui.checkWorkers,
        'Diagnostics',
        'Verify compiler & analyzer status across .NET, TypeScript, Python, Rust, Go, Java, C++, PHP, Dart & Kotlin',
        'check-all',
        'aiPromptContextBuilder.checkWorkers'
      ),
      new ActionTreeItem(
        ui.changeLanguage,
        `${lang.flag} ${lang.nativeName} (${lang.badge})`,
        `Current Language: ${lang.flag} ${lang.nativeName} — ${lang.regionSubtitle}. Click to switch language (EN, ES, FR, DE, ZH, JA, PT, RU, ZU, AF).`,
        'globe',
        'aiPromptContextBuilder.changeLanguage'
      ),
    ];
  }
}

async function promptAndChangeLanguage() {
  const current = getActiveLanguageOption();

  const items = LANGUAGE_CATALOG.map((l) => ({
    label: `${l.flag}  ${l.nativeName}`,
    description: l.code === current.code ? `${l.badge}   $(check) Active` : `${l.badge}   Select`,
    detail: l.regionSubtitle,
    option: l,
  }));

  const pick = await vscode.window.showQuickPick(items, {
    title: `Change Language — Language • ${current.flag} ${current.nativeName}`,
    placeHolder: 'Select language for Sidebar UI, AI Prompt Explanations & Git Commit Drafts',
  });
  if (!pick) return;

  const chosen = pick.option;
  if (extContext) {
    await extContext.globalState.update('outputLanguage', chosen.code);
  }
  try {
    await vscode.workspace
      .getConfiguration('aiPromptContextBuilder')
      .update('outputLanguage', chosen.code, vscode.ConfigurationTarget.Global);
  } catch {
    // ignore config write errors if setting is not registered in host yet
  }

  if (actionsTreeProvider) {
    actionsTreeProvider.refresh();
  }

  const newUi = getUiStrings();
  vscode.window.showInformationMessage(newUi.languageChangedMsg(chosen));
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

async function promptAndDraftCommitMessage(filePath?: string) {
  const workspaceFolder =
    vscode.workspace.workspaceFolders?.[0]?.uri.fsPath ||
    (filePath ? path.dirname(filePath) : undefined);

  if (!workspaceFolder) {
    vscode.window.showErrorMessage('Please open a workspace folder inside a Git repository.');
    return;
  }

  // Popup 1: Select Uncommitted Changes Scope
  const scopePick = await vscode.window.showQuickPick(
    [
      {
        label: '$(check-all) Auto-Detect (Staged if any, else All Uncommitted)',
        description: 'Recommended',
        detail: 'Drafts commit message for staged files if you ran git add, otherwise all uncommitted changes',
        flag: '',
      },
      {
        label: '$(diff-added) Staged Changes ONLY',
        description: 'git diff --cached',
        detail: 'Draft commit message strictly for currently staged files',
        flag: '--staged',
      },
      {
        label: '$(git-merge) All Uncommitted Changes (Staged + Unstaged + Untracked)',
        description: 'Full Working Tree',
        detail: 'Draft commit message covering all modified, added, and untracked files',
        flag: '--git-all',
      },
      {
        label: '$(diff-modified) Unstaged Changes ONLY',
        description: 'git diff',
        detail: 'Draft commit message strictly for unstaged working tree modifications',
        flag: '--git-unstaged',
      },
    ],
    {
      title: 'Draft Git Commit Message — Step 1/2: Select Scope',
      placeHolder: 'Choose which uncommitted changes to summarize',
    }
  );
  if (!scopePick) return;

  // Popup 2: Select Drafting Engine (Deterministic AST vs VS Code Built-in LM vs Local Ollama)
  const enginePick = await vscode.window.showQuickPick(
    [
      {
        label: '$(zap) Instant AST & Diff Analyzer (No AI)',
        description: '100% Deterministic & Offline (< 20ms)',
        detail: 'Extracts added/modified classes, methods & functions from diff hunks into a Conventional Commit',
        engine: 'ast' as const,
      },
      {
        label: '$(sparkle) VS Code Built-in Language Model (vscode.lm) + AST',
        description: 'Zero-Config Editor Model (with AST fallback)',
        detail: 'Uses VS Code built-in vscode.lm API to polish the AST diff summary (falls back to AST if unavailable)',
        engine: 'vscodelm' as const,
      },
      {
        label: '$(hubot) Local Ollama Tiny LLM (localhost:11434) + AST',
        description: 'Local CPU/GPU Model (with AST fallback)',
        detail: 'Uses a local Ollama model on 127.0.0.1:11434 if running, otherwise falls back to AST rules',
        engine: 'ollama' as const,
      },
    ],
    {
      title: 'Draft Git Commit Message — Step 2/2: Select Drafting Engine',
      placeHolder: 'Choose Instant AST Rules (No AI) or Local/Built-in Language Model',
    }
  );
  if (!enginePick) return;

  await vscode.window.withProgress(
    {
      location: vscode.ProgressLocation.Notification,
      title: 'Drafting Git Commit Message...',
      cancellable: true,
    },
    async (progress, token) => {
      progress.report({ message: 'Analyzing Git diff & AST symbols...' });

      const activeLang = getActiveLanguageOption();
      const args = ['draft-commit', '--workspace', workspaceFolder, '--format', 'json'];
      if (scopePick.flag) {
        args.push(scopePick.flag);
      }
      if (activeLang.code !== 'en') {
        args.push('--output-language', activeLang.code);
      }

      const spec = resolveCommand(args);
      outputChannel.appendLine(`[DraftCommit] ${spec.command} ${spec.args.join(' ')}`);

      const draftJson = await new Promise<any>((resolve, reject) => {
        const proc = spawn(spec.command, spec.args, spec.options);
        let stdout = '';
        let stderr = '';
        proc.stdout.on('data', (d) => { stdout += d.toString(); });
        proc.stderr.on('data', (d) => { stderr += d.toString(); });
        proc.on('error', async (err) => {
          await handleMissingRuntime(spec.command, err);
          reject(err);
        });
        proc.on('close', () => {
          try {
            resolve(JSON.parse(stdout));
          } catch (e) {
            reject(new Error(stderr || stdout || 'Failed to parse commit draft output.'));
          }
        });
      });

      if (!draftJson || !draftJson.success) {
        vscode.window.showWarningMessage(draftJson?.message || 'No uncommitted Git changes found to draft a commit message.');
        return;
      }

      let finalMessage: string = draftJson.fullCommitMessage;
      let engineUsed = 'AST + Diff Rules (No AI)';

      if (enginePick.engine === 'vscodelm') {
        progress.report({ message: 'Polishing commit message via VS Code built-in model (vscode.lm)...' });
        const lmRefined = await tryRefineWithVsCodeLm(draftJson, token);
        if (lmRefined) {
          finalMessage = lmRefined;
          engineUsed = 'VS Code Built-in Model (vscode.lm)';
        } else {
          outputChannel.appendLine('[DraftCommit] vscode.lm not available or returned empty; using deterministic AST draft.');
        }
      } else if (enginePick.engine === 'ollama') {
        progress.report({ message: 'Checking local Ollama model (127.0.0.1:11434)...' });
        const ollamaRefined = await tryRefineWithLocalOllama(draftJson);
        if (ollamaRefined) {
          finalMessage = ollamaRefined;
          engineUsed = 'Local Ollama LLM';
        } else {
          outputChannel.appendLine('[DraftCommit] Local Ollama not running on 127.0.0.1:11434; using deterministic AST draft.');
        }
      }

      // 1. Populate VS Code Source Control Commit Input Box if available
      const filledScm = await setVsCodeGitCommitInputBox(finalMessage);

      // 2. Always copy to clipboard for immediate Ctrl+V convenience
      await vscode.env.clipboard.writeText(finalMessage);

      const statusNote = filledScm
        ? `Commit message drafted via ${engineUsed}, filled into Source Control box & copied to clipboard!`
        : `Commit message drafted via ${engineUsed} & copied to clipboard!`;

      const action = await vscode.window.showInformationMessage(
        statusNote,
        'Open Source Control',
        'Preview / Edit in Editor'
      );

      if (action === 'Open Source Control') {
        await vscode.commands.executeCommand('workbench.view.scm');
      } else if (action === 'Preview / Edit in Editor') {
        const doc = await vscode.workspace.openTextDocument({
          content: finalMessage,
          language: 'git-commit',
        });
        await vscode.window.showTextDocument(doc);
      }
    }
  );
}

async function setVsCodeGitCommitInputBox(message: string): Promise<boolean> {
  try {
    const gitExt = vscode.extensions.getExtension('vscode.git');
    if (!gitExt) return false;
    const gitApi = gitExt.isActive ? gitExt.exports.getAPI(1) : (await gitExt.activate()).getAPI(1);
    if (gitApi && gitApi.repositories && gitApi.repositories.length > 0) {
      gitApi.repositories[0].inputBox.value = message;
      return true;
    }
  } catch {
    // ignore if git extension is unavailable
  }
  return false;
}

async function tryRefineWithVsCodeLm(draftJson: any, token: vscode.CancellationToken): Promise<string | undefined> {
  try {
    const lmAny = (vscode as any).lm;
    const ChatMessage = (vscode as any).LanguageModelChatMessage;
    if (!lmAny || typeof lmAny.selectChatModels !== 'function' || !ChatMessage) {
      return undefined;
    }

    const models = await lmAny.selectChatModels({});
    if (!models || models.length === 0) {
      return undefined;
    }

    const model = models[0];
    const prompt = buildCommitRefinePrompt(draftJson);
    const messages = [ChatMessage.User(prompt)];
    const response = await model.sendRequest(messages, {}, token);

    let text = '';
    for await (const chunk of response.text) {
      text += chunk;
    }
    const cleaned = cleanCodeFences(text);
    return cleaned.length > 10 ? cleaned : undefined;
  } catch {
    return undefined;
  }
}

async function tryRefineWithLocalOllama(draftJson: any): Promise<string | undefined> {
  try {
    const tagsJson: any = await httpGetJson('http://127.0.0.1:11434/api/tags', 1500);
    const models: any[] = tagsJson?.models || [];
    if (models.length === 0) return undefined;

    const modelName = models[0].name;
    const prompt = buildCommitRefinePrompt(draftJson);
    const resJson: any = await httpPostJson(
      'http://127.0.0.1:11434/api/generate',
      { model: modelName, prompt, stream: false },
      8000
    );
    const cleaned = cleanCodeFences(resJson?.response || '');
    return cleaned.length > 10 ? cleaned : undefined;
  } catch {
    return undefined;
  }
}

function buildCommitRefinePrompt(draftJson: any): string {
  const activeLang = getActiveLanguageOption();
  const langInstruction =
    activeLang.code === 'en'
      ? 'Write the commit description in English.'
      : `Write the commit subject description and bullet points in ${activeLang.nativeName} (${activeLang.englishName}), while keeping the Conventional Commit type prefix (e.g. feat, fix, refactor) and code symbol names intact.`;

  return [
    'Write a clean, concise Conventional Commit message (subject line + bullet-point body) for the following uncommitted changes.',
    langInstruction,
    'Output ONLY the raw commit message text without markdown code fences or commentary.',
    '',
    'AST Baseline Draft:',
    draftJson.fullCommitMessage,
    '',
    'Diff Excerpt:',
    draftJson.compactDiffForLlm || '',
  ].join('\n');
}

function cleanCodeFences(raw: string): string {
  return raw
    .trim()
    .replace(/^```[a-zA-Z0-9_-]*\r?\n/, '')
    .replace(/\r?\n```$/, '')
    .trim();
}

function httpGetJson(url: string, timeoutMs: number): Promise<any> {
  return new Promise((resolve, reject) => {
    const req = http.get(url, { timeout: timeoutMs }, (res) => {
      let data = '';
      res.on('data', (c) => { data += c; });
      res.on('end', () => {
        try { resolve(JSON.parse(data)); } catch (e) { reject(e); }
      });
    });
    req.on('error', reject);
    req.on('timeout', () => { req.destroy(); reject(new Error('timeout')); });
  });
}

function httpPostJson(url: string, bodyObj: any, timeoutMs: number): Promise<any> {
  return new Promise((resolve, reject) => {
    const payload = JSON.stringify(bodyObj);
    const parsedUrl = new URL(url);
    const req = http.request(
      {
        hostname: parsedUrl.hostname,
        port: parsedUrl.port,
        path: parsedUrl.pathname,
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          'Content-Length': Buffer.byteLength(payload),
        },
        timeout: timeoutMs,
      },
      (res) => {
        let data = '';
        res.on('data', (c) => { data += c; });
        res.on('end', () => {
          try { resolve(JSON.parse(data)); } catch (e) { reject(e); }
        });
      }
    );
    req.on('error', reject);
    req.on('timeout', () => { req.destroy(); reject(new Error('timeout')); });
    req.write(payload);
    req.end();
  });
}
