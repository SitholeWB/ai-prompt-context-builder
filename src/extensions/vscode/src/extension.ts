import * as vscode from 'vscode';
import { spawn, execSync } from 'child_process';
import * as path from 'path';
import * as fs from 'fs';
import * as http from 'http';

type GitScopeType = 'none' | 'all' | 'staged' | 'unstaged' | 'file' | 'branch' | 'commits' | 'draftCommit' | 'graphify';
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
  graphifyCodebase: string;
  graphifySubtitle: string;
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
    graphifyCodebase: 'Graphify: Codebase Knowledge Graph...',
    graphifySubtitle: 'Interactive Graph & AI Map',
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
    graphifyCodebase: 'Graphify: Grafo de Conocimiento...',
    graphifySubtitle: 'Grafo Interactivo + Mapa IA',
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
    graphifyCodebase: 'Graphify : Graphe de Connaissances...',
    graphifySubtitle: 'Graphe Interactif & Carte IA',
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
    graphifyCodebase: 'Graphify: Codebase-Wissensgraph...',
    graphifySubtitle: 'Interaktiver Graph & KI-Karte',
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
    graphifyCodebase: 'Graphify: 全代码库知识图谱...',
    graphifySubtitle: '可视化图谱 + AI 架构地图',
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
    graphifyCodebase: 'Graphify: コードベース知識グラフ...',
    graphifySubtitle: '対話型グラフ & AIマップ',
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
    graphifyCodebase: 'Graphify: Grafo de Conhecimento...',
    graphifySubtitle: 'Grafo Interativo & Mapa IA',
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
    graphifyCodebase: 'Graphify: Граф знаний кодовой базы...',
    graphifySubtitle: 'Интерактивный граф и AI-карта',
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
    graphifyCodebase: 'Graphify: Igrafu Yolwazi lwe-Codebase...',
    graphifySubtitle: 'Igrafu Ebonakalayo + Imephu ye-AI',
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
    graphifyCodebase: 'Graphify: Kodebasis-Kennisgrafiek...',
    graphifySubtitle: 'Interaktiewe Grafiek & KI-Kaart',
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

  // 1g. Graphify: Build Interactive Knowledge Graph & AI Architecture Map
  registerCmd('graphifyCodebase', async (uri?: vscode.Uri) => {
    const filePath = resolveTargetFilePath(uri);
    await promptAndRunGraphify(filePath);
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

  // Detect word / symbol at cursor if active editor is editing this file
  let cursorSymbol: string | undefined;
  const activeEditor = vscode.window.activeTextEditor;
  if (activeEditor && (!filePath || activeEditor.document.uri.fsPath === filePath)) {
    const sel = activeEditor.selection;
    let s = activeEditor.document.getText(sel).trim();
    if (!s) {
      const wordRange = activeEditor.document.getWordRangeAtPosition(sel.active);
      if (wordRange) s = activeEditor.document.getText(wordRange).trim();
    }
    if (s && /^[a-zA-Z_][a-zA-Z0-9_]*$/.test(s)) {
      cursorSymbol = s;
    }
  }

  const fileGraphifyItem: ScopeQuickPickItem | undefined = filePath
    ? {
        label: `$(type-hierarchy) Graphify: Visualize Dependencies & Architecture (${path.basename(filePath)})`,
        description: 'Interactive Force Graph & AI Map',
        detail: `Open interactive visual graph centered on ${path.basename(filePath)} and its dependency network`,
        gitScope: 'graphifyFile' as any,
      }
    : undefined;

  const fileItem: ScopeQuickPickItem | undefined = filePath
    ? {
        label: '$(file-code) Dependencies Only (No Git Changes)',
        description: `Classic AI Context (${path.basename(filePath)})`,
        detail: 'Trace full AST dependency tree of the selected file without Git diffs',
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

  const cursorItem: ScopeQuickPickItem | undefined = (filePath && cursorSymbol)
    ? {
        label: `$(symbol-class) Symbol at Cursor: "${cursorSymbol}" + Dependencies`,
        description: `Targeted AST Root (${path.basename(filePath)})`,
        detail: `Focus AST dependency traversal starting strictly from symbol "${cursorSymbol}"`,
        gitScope: 'symbol' as any,
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
      label: '$(type-hierarchy) Graphify: Entire Codebase Knowledge Graph (Interactive + AI Map)...',
      description: 'PageRank Hubs, Cycles & Visual Map',
      detail: 'Build an interactive zoomable Knowledge Graph & compact AI Architecture Map of the entire codebase (~95% token savings)',
      gitScope: 'graphify',
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
    if (fileGraphifyItem) scopeItems.push(fileGraphifyItem);
    if (cursorItem) scopeItems.push(cursorItem);
  } else {
    if (fileGraphifyItem) scopeItems.push(fileGraphifyItem);
    if (fileItem) scopeItems.push(fileItem);
    if (singleFileGitItem) scopeItems.push(singleFileGitItem);
    if (cursorItem) scopeItems.push(cursorItem);
    const codebaseGraphify = gitItems.find((i) => i.gitScope === 'graphify');
    if (codebaseGraphify) scopeItems.push(codebaseGraphify);
    scopeItems.push(...gitItems.filter((i) => i.gitScope !== 'graphify'));
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
        ? `Graphify — Codebase Graph & AI Context: ${path.basename(filePath)}`
        : 'Graphify — Codebase Graph & AI Context: Git & Workspace Scopes',
      placeHolder: 'Select Context Scope (Interactive Graph, File Dependencies, Git Scopes)',
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

  if ((chosenScope as string) === 'graphifyFile') {
    await promptAndRunGraphify(filePath, filePath ? path.basename(filePath) : undefined);
    return;
  } else if ((chosenScope as string) === 'symbol') {
    const targetPath = filePath || workspaceFolder || '';
    const baseOptions: RunOptions = {
      symbol: cursorSymbol,
      gitScope: 'none',
      includeGitDiff: false,
    };
    await runGeneration(targetPath, baseOptions);
    return;
  } else if (chosenScope === 'graphify') {
    await promptAndRunGraphify(filePath);
    return;
  } else if (chosenScope === 'draftCommit') {
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

  // Popup 3: Export with Defaults vs Graphify vs Custom Options
  const ui = getUiStrings();
  const activeLang = getActiveLanguageOption();
  interface ExportModeQuickPickItem extends vscode.QuickPickItem {
    actionType: 'defaults' | 'graphify' | 'custom';
  }

  const modePick = await vscode.window.showQuickPick<ExportModeQuickPickItem>(
    [
      {
        label: ui.exportDefaults,
        description: `Instant Generation • ${activeLang.flag} ${activeLang.nativeName}`,
        detail: 'Full dependency graph, automatic token budget, comments preserved, tools/lockfiles excluded',
        actionType: 'defaults',
      },
      {
        label: '$(type-hierarchy) Graphify: Open Interactive Knowledge Graph',
        description: `Visual Force Canvas & Architecture Map${filePath ? ` (${path.basename(filePath)})` : ''}`,
        detail: 'Launch the interactive zoomable visual graph dashboard with PageRank God Nodes, circular cycles & clusters',
        actionType: 'graphify',
      },
      {
        label: ui.customOptions,
        description: 'Step-by-Step Configuration',
        detail: 'Configure token budget, comment stripping, traversal depth, output language, destination & custom prompt',
        actionType: 'custom',
      },
    ],
    {
      title: `${ui.chooseExportMode} (${activeLang.flag} ${activeLang.nativeName})`,
      placeHolder: 'Instant Defaults, Graphify Visualizer, or Step-by-Step Custom Options',
    }
  );
  if (!modePick) return;

  if (modePick.actionType === 'graphify') {
    await promptAndRunGraphify(filePath, filePath ? path.basename(filePath) : undefined);
    return;
  }

  const baseOptions: RunOptions = {
    gitScope: chosenScope,
    includeGitDiff,
    workspacePath: workspaceFolder,
    compareBranch,
    commitCount,
    outputLanguage: activeLang.code,
  };

  const targetPath = filePath || workspaceFolder || '';

  if (modePick.actionType === 'defaults') {
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
        ui.graphifyCodebase,
        ui.graphifySubtitle,
        'Build an interactive force-directed Knowledge Graph (PageRank God Nodes, Circular Cycles, Domain Clusters) & compact AI Architecture Map of the entire codebase',
        'type-hierarchy',
        'aiPromptContextBuilder.graphifyCodebase'
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

async function promptAndRunGraphify(filePath?: string, focusFile?: string) {
  const workspaceFolder =
    vscode.workspace.workspaceFolders?.[0]?.uri.fsPath ||
    (filePath ? path.dirname(filePath) : undefined);

  if (!workspaceFolder) {
    vscode.window.showErrorMessage('Please open a workspace folder or file to build the Graphify Knowledge Graph.');
    return;
  }

  // Step 1: Select Graphify Scope
  const scopePick = await vscode.window.showQuickPick(
    [
      ...(focusFile
        ? [
            {
              label: `$(type-hierarchy) Focus on ${focusFile} & Transitive Dependencies`,
              description: 'Targeted Dependency Network',
              detail: `Builds the whole Knowledge Graph and automatically highlights, centers and inspects ${focusFile}`,
              includeTests: false,
              gitFlag: '',
            },
          ]
        : []),
      {
        label: '$(type-hierarchy) Entire Codebase (Production Source Files)',
        description: 'Recommended • Fast Whole-Repo AST Graph',
        detail: 'Indexes all source files across the codebase, computes PageRank God Nodes, circular cycles & domain clusters',
        includeTests: false,
        gitFlag: '',
      },
      {
        label: '$(git-merge) Entire Codebase + Highlight Uncommitted Git Changes',
        description: 'Git-Aware Architecture Graph',
        detail: 'Builds the whole codebase graph and highlights all uncommitted/modified files in emerald green',
        includeTests: false,
        gitFlag: '--git',
      },
      {
        label: '$(beaker) Entire Codebase + Test Files',
        description: 'Include Unit & Integration Tests',
        detail: 'Includes test files and fixtures in the Knowledge Graph',
        includeTests: true,
        gitFlag: '',
      },
    ],
    {
      title: focusFile
        ? `Graphify: ${focusFile} — Step 1/2: Select Scope`
        : 'Graphify Knowledge Graph — Step 1/2: Select Codebase Scope',
      placeHolder: 'Choose which files to include in the Knowledge Graph',
    }
  );
  if (!scopePick) return;

  // Step 2: Select Output Mode
  const activeLang = getActiveLanguageOption();
  const modePick = await vscode.window.showQuickPick(
    [
      {
        label: '$(graph) Interactive Visual Graph + AI Knowledge Map Markdown',
        description: `Recommended • ${activeLang.flag} ${activeLang.nativeName}`,
        detail: 'Opens the interactive zoomable Graphify Visualizer in VS Code AND saves codebase-knowledge-graph.md (~95% token savings)',
        openVisualizer: true,
        saveMarkdown: true,
      },
      {
        label: '$(eye) Interactive Visual Knowledge Graph Only (VS Code Dashboard)',
        description: 'Force-Directed Node Explorer',
        detail: 'Open the zoomable Graphify architecture explorer tab to inspect God Nodes, cycles, clusters, and jump to files',
        openVisualizer: true,
        saveMarkdown: false,
      },
      {
        label: '$(markdown) AI Knowledge Map Markdown Only (codebase-knowledge-graph.md)',
        description: 'For ChatGPT, Claude, Copilot & DeepSeek',
        detail: 'Generate the token-efficient AST & Adjacency Knowledge Map + Mermaid diagram and open in editor',
        openVisualizer: false,
        saveMarkdown: true,
      },
    ],
    {
      title: 'Graphify Knowledge Graph — Step 2/2: Select Output View',
      placeHolder: 'Open Interactive Graph Visualizer, AI Knowledge Map Markdown, or Both',
    }
  );
  if (!modePick) return;

  await vscode.window.withProgress(
    {
      location: vscode.ProgressLocation.Notification,
      title: 'Graphify: Building Codebase Knowledge Graph...',
      cancellable: true,
    },
    async (progress, token) => {
      progress.report({ message: 'Scanning workspace AST, PageRank hubs & Tarjan cycles...' });

      const args = [
        'graphify',
        '--workspace',
        workspaceFolder,
        '--format',
        'json',
        '--include-tests',
        scopePick.includeTests ? 'true' : 'false',
        '--save',
        modePick.saveMarkdown ? 'true' : 'false',
      ];
      if (scopePick.gitFlag) {
        args.push(scopePick.gitFlag);
      }
      if (activeLang.code !== 'en') {
        args.push('--output-language', activeLang.code);
      }

      const spec = resolveCommand(args);
      outputChannel.appendLine(`[Graphify] ${spec.command} ${spec.args.join(' ')}`);

      const graphRes = await new Promise<any>((resolve, reject) => {
        const proc = spawn(spec.command, spec.args, spec.options);
        token.onCancellationRequested(() => proc.kill());

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
          } catch {
            reject(new Error(stderr || stdout || 'Failed to parse Graphify output.'));
          }
        });
      });

      if (!graphRes || !graphRes.success) {
        vscode.window.showErrorMessage(`Graphify failed: ${graphRes?.message || 'No supported source files found.'}`);
        return;
      }

      if (graphRes.outputPath && historyTreeProvider) {
        lastGeneratedPath = graphRes.outputPath;
        historyTreeProvider.addGeneratedFile(graphRes.outputPath, graphRes.totalFiles, graphRes.graphMarkdownTokens);
      }

      if (modePick.openVisualizer) {
        openGraphifyWebviewPanel(graphRes, workspaceFolder, focusFile);
      } else if (graphRes.outputPath && fs.existsSync(graphRes.outputPath)) {
        const doc = await vscode.workspace.openTextDocument(graphRes.outputPath);
        await vscode.window.showTextDocument(doc);
      }

      const actionButtons = ['Copy AI Knowledge Map'];
      if (graphRes.outputPath) actionButtons.unshift('Open Markdown Map');

      vscode.window
        .showInformationMessage(
          `Graphify Complete: ${graphRes.totalFiles} files, ${graphRes.totalEdges} edges, ${graphRes.godNodes?.length || 0} God Nodes (~${graphRes.tokenSavingsPercent}% token savings).`,
          ...actionButtons
        )
        .then(async (choice) => {
          if (choice === 'Open Markdown Map' && graphRes.outputPath && fs.existsSync(graphRes.outputPath)) {
            const doc = await vscode.workspace.openTextDocument(graphRes.outputPath);
            await vscode.window.showTextDocument(doc, vscode.ViewColumn.Beside);
          } else if (choice === 'Copy AI Knowledge Map' && graphRes.generatedMarkdown) {
            await vscode.env.clipboard.writeText(graphRes.generatedMarkdown);
            vscode.window.showInformationMessage('Codebase Knowledge Map copied to clipboard!');
          }
        });
    }
  );
}

function openGraphifyWebviewPanel(graphRes: any, workspaceFolder: string, initialFocusFile?: string) {
  const panel = vscode.window.createWebviewPanel(
    'aiPromptContextBuilderGraphify',
    `Graphify: ${graphRes.workspaceName || 'Codebase'} (${graphRes.totalFiles} files)`,
    vscode.ViewColumn.One,
    {
      enableScripts: true,
      retainContextWhenHidden: true,
    }
  );

  panel.webview.html = buildGraphifyWebviewHtml(graphRes, initialFocusFile);

  panel.webview.onDidReceiveMessage(async (msg) => {
    if (msg.command === 'openFile' && msg.relativePath) {
      const absPath = path.join(workspaceFolder, msg.relativePath);
      if (fs.existsSync(absPath)) {
        const doc = await vscode.workspace.openTextDocument(absPath);
        await vscode.window.showTextDocument(doc, vscode.ViewColumn.Beside);
      }
    } else if (msg.command === 'generateFilePrompt' && msg.relativePath) {
      const absPath = path.join(workspaceFolder, msg.relativePath);
      if (fs.existsSync(absPath)) {
        await runGeneration(absPath, { workspacePath: workspaceFolder });
      }
    } else if (msg.command === 'openMarkdown') {
      if (graphRes.outputPath && fs.existsSync(graphRes.outputPath)) {
        const doc = await vscode.workspace.openTextDocument(graphRes.outputPath);
        await vscode.window.showTextDocument(doc, vscode.ViewColumn.Beside);
      } else if (graphRes.generatedMarkdown) {
        const doc = await vscode.workspace.openTextDocument({
          content: graphRes.generatedMarkdown,
          language: 'markdown',
        });
        await vscode.window.showTextDocument(doc, vscode.ViewColumn.Beside);
      }
    } else if (msg.command === 'copyMarkdown' && graphRes.generatedMarkdown) {
      await vscode.env.clipboard.writeText(graphRes.generatedMarkdown);
      vscode.window.showInformationMessage('Graphify AI Knowledge Map copied to clipboard!');
    }
  });
}

function buildGraphifyWebviewHtml(graphRes: any, initialFocusFile?: string): string {
  const safeJson = JSON.stringify({
    workspaceName: graphRes.workspaceName,
    scopeDescription: graphRes.scopeDescription,
    totalFiles: graphRes.totalFiles,
    totalEdges: graphRes.totalEdges,
    totalSymbols: graphRes.totalSymbols,
    rawCodebaseTokens: graphRes.rawCodebaseTokens,
    graphMarkdownTokens: graphRes.graphMarkdownTokens,
    tokenSavingsPercent: graphRes.tokenSavingsPercent,
    nodes: graphRes.nodes || [],
    edges: graphRes.edges || [],
    godNodes: graphRes.godNodes || [],
    circularCycles: graphRes.circularCycles || [],
    clusters: graphRes.clusters || [],
  }).replace(/</g, '\\u003c');

  return `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8" />
<meta name="viewport" content="width=device-width, initial-scale=1.0" />
<title>Graphify Knowledge Graph</title>
<style>
  :root {
    --bg: #0b0f19;
    --panel: #111827;
    --panel-border: #1f2937;
    --text: #f3f4f6;
    --muted: #9ca3af;
    --accent: #38bdf8;
    --god: #f59e0b;
    --mod: #10b981;
    --cycle: #ef4444;
  }
  * { box-sizing: border-box; margin: 0; padding: 0; }
  body {
    background: var(--bg);
    color: var(--text);
    font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
    height: 100vh;
    display: flex;
    flex-direction: column;
    overflow: hidden;
  }
  .topbar {
    display: flex;
    align-items: center;
    justify-content: space-between;
    padding: 10px 16px;
    background: var(--panel);
    border-bottom: 1px solid var(--panel-border);
    gap: 12px;
    flex-wrap: wrap;
  }
  .brand {
    display: flex;
    align-items: center;
    gap: 10px;
  }
  .brand-title {
    font-weight: 700;
    font-size: 14px;
    color: #fff;
  }
  .metrics {
    display: flex;
    gap: 10px;
    flex-wrap: wrap;
  }
  .pill {
    background: #1e293b;
    border: 1px solid #334155;
    padding: 4px 10px;
    border-radius: 999px;
    font-size: 12px;
    color: #e2e8f0;
  }
  .pill b { color: var(--accent); }
  .pill.god b { color: var(--god); }
  .pill.savings b { color: var(--mod); }
  .controls {
    display: flex;
    align-items: center;
    gap: 8px;
  }
  input.search {
    background: #0f172a;
    border: 1px solid #334155;
    color: #fff;
    padding: 5px 10px;
    border-radius: 6px;
    font-size: 12px;
    width: 210px;
  }
  select.filter {
    background: #0f172a;
    border: 1px solid #334155;
    color: #fff;
    padding: 5px 8px;
    border-radius: 6px;
    font-size: 12px;
  }
  button.btn {
    background: #1e293b;
    border: 1px solid #3b82f6;
    color: #f8fafc;
    padding: 5px 10px;
    border-radius: 6px;
    font-size: 12px;
    cursor: pointer;
    transition: background 0.15s;
  }
  button.btn:hover { background: #2563eb; }
  button.btn.primary { background: #2563eb; border-color: #60a5fa; font-weight: 600; }
  .workspace {
    display: flex;
    flex: 1;
    overflow: hidden;
    position: relative;
  }
  .canvas-wrap {
    flex: 1;
    position: relative;
    overflow: hidden;
  }
  canvas {
    width: 100%;
    height: 100%;
    display: block;
    cursor: grab;
  }
  canvas:active { cursor: grabbing; }
  .legend {
    position: absolute;
    bottom: 14px;
    left: 14px;
    background: rgba(17, 24, 39, 0.9);
    border: 1px solid #374151;
    border-radius: 8px;
    padding: 8px 12px;
    display: flex;
    gap: 14px;
    font-size: 11px;
    color: #d1d5db;
    pointer-events: none;
  }
  .dot {
    display: inline-block;
    width: 9px;
    height: 9px;
    border-radius: 50%;
    margin-right: 5px;
  }
  .sidebar {
    width: 340px;
    background: var(--panel);
    border-left: 1px solid var(--panel-border);
    padding: 14px;
    overflow-y: auto;
    display: flex;
    flex-direction: column;
    gap: 12px;
  }
  .card {
    background: #1e293b;
    border: 1px solid #334155;
    border-radius: 8px;
    padding: 10px 12px;
  }
  .card h3 {
    font-size: 12px;
    text-transform: uppercase;
    letter-spacing: 0.05em;
    color: var(--muted);
    margin-bottom: 8px;
  }
  .node-title {
    font-size: 14px;
    font-weight: 700;
    color: #fff;
    word-break: break-all;
    margin-bottom: 4px;
  }
  .node-path {
    font-size: 11px;
    color: var(--accent);
    word-break: break-all;
    margin-bottom: 8px;
  }
  .tag-list {
    display: flex;
    flex-wrap: wrap;
    gap: 5px;
    margin-top: 6px;
  }
  .tag {
    background: #0f172a;
    border: 1px solid #334155;
    border-radius: 4px;
    padding: 2px 6px;
    font-size: 11px;
    color: #cbd5e1;
    cursor: pointer;
  }
  .tag:hover { border-color: var(--accent); color: #fff; }
  .stat-grid {
    display: grid;
    grid-template-columns: 1fr 1fr;
    gap: 6px;
    font-size: 12px;
  }
  .btn-row {
    display: flex;
    gap: 6px;
    margin-top: 10px;
  }
  .btn-row button { flex: 1; }
</style>
</head>
<body>
  <div class="topbar">
    <div class="brand">
      <span class="brand-title">🕸️ Graphify Knowledge Graph — <span id="wsName"></span></span>
      <div class="metrics">
        <span class="pill"><b id="mFiles">0</b> Files</span>
        <span class="pill"><b id="mEdges">0</b> Edges</span>
        <span class="pill god"><b id="mGods">0</b> ★ God Nodes</span>
        <span class="pill savings"><b id="mSavings">0%</b> Token Savings</span>
      </div>
    </div>
    <div class="controls">
      <input id="searchInput" class="search" type="text" placeholder="Search file or AST symbol..." />
      <select id="filterSelect" class="filter">
        <option value="all">All Nodes</option>
        <option value="god">★ God Nodes (Core Hubs)</option>
        <option value="modified">● Git Modified</option>
        <option value="cycle">⚠ Circular Cycles</option>
      </select>
      <button class="btn" id="resetViewBtn">Reset Zoom</button>
      <button class="btn" id="copyMdBtn">📋 Copy AI Map</button>
      <button class="btn primary" id="openMdBtn">📄 Open AI Map (.md)</button>
    </div>
  </div>
  <div class="workspace">
    <div class="canvas-wrap">
      <canvas id="graphCanvas"></canvas>
      <div class="legend">
        <span><i class="dot" style="background:#f59e0b"></i>★ God Node (Hub)</span>
        <span><i class="dot" style="background:#10b981"></i>● Git Modified</span>
        <span><i class="dot" style="background:#ef4444"></i>⚠ In Cycle</span>
        <span><i class="dot" style="background:#38bdf8"></i>Standard Module (Sized by PageRank)</span>
      </div>
    </div>
    <div class="sidebar">
      <div class="card" id="inspectorCard">
        <h3>Selected Node Inspector</h3>
        <div id="inspectorBody">
          <p style="font-size:12px;color:#9ca3af;">Click any node in the graph to inspect its AST symbols, PageRank centrality, incoming callers, and outgoing dependencies.</p>
        </div>
      </div>
      <div class="card">
        <h3>★ Top God Nodes (Core Bottlenecks)</h3>
        <div id="godList" class="tag-list"></div>
      </div>
      <div class="card">
        <h3>Domain / Module Clusters</h3>
        <div id="clusterList" class="tag-list"></div>
      </div>
    </div>
  </div>

<script>
  const vscode = acquireVsCodeApi();
  const data = ${safeJson};

  document.getElementById('wsName').textContent = data.workspaceName || 'Workspace';
  document.getElementById('mFiles').textContent = data.totalFiles;
  document.getElementById('mEdges').textContent = data.totalEdges;
  document.getElementById('mGods').textContent = (data.godNodes || []).length;
  document.getElementById('mSavings').textContent = '~' + (data.tokenSavingsPercent || 0) + '%';

  const clusterColors = ['#38bdf8', '#a78bfa', '#34d399', '#f472b6', '#60a5fa', '#fb923c', '#2dd4bf', '#e879f9'];
  const clusterColorMap = {};
  (data.clusters || []).forEach((c, idx) => {
    clusterColorMap[c.name] = clusterColors[idx % clusterColors.length];
    const opt = document.createElement('option');
    opt.value = 'cluster:' + c.name;
    opt.textContent = 'Cluster: ' + c.name + ' (' + c.fileCount + ')';
    document.getElementById('filterSelect').appendChild(opt);
  });

  const canvas = document.getElementById('graphCanvas');
  const ctx = canvas.getContext('2d');
  let width = 800, height = 600;
  let camX = 0, camY = 0, camScale = 1;

  const nodes = (data.nodes || []).map((n, i) => {
    const angle = (i / Math.max(1, data.nodes.length)) * Math.PI * 2;
    const radius = 140 + (i % 7) * 35;
    return {
      ...n,
      x: Math.cos(angle) * radius,
      y: Math.sin(angle) * radius,
      vx: 0,
      vy: 0,
      r: Math.max(7, Math.min(22, 6 + (n.pageRank || 10) * 0.14))
    };
  });

  const nodeById = {};
  nodes.forEach(n => { nodeById[n.id] = n; });

  const edges = (data.edges || []).map(e => ({
    source: nodeById[e.source],
    target: nodeById[e.target],
    relationship: e.relationship
  })).filter(e => e.source && e.target);

  let selectedNode = nodes[0] || null;
  let hoveredNode = null;
  let draggedNode = null;
  let isPanning = false;
  let panStartX = 0, panStartY = 0;
  let searchQuery = '';
  let activeFilter = 'all';

  function resize() {
    const rect = canvas.parentElement.getBoundingClientRect();
    width = rect.width;
    height = rect.height;
    canvas.width = width * window.devicePixelRatio;
    canvas.height = height * window.devicePixelRatio;
    ctx.setTransform(window.devicePixelRatio, 0, 0, window.devicePixelRatio, 0, 0);
  }
  window.addEventListener('resize', resize);
  resize();

  function isNodeVisible(n) {
    if (activeFilter === 'god' && !n.isGodNode) return false;
    if (activeFilter === 'modified' && !n.isModified) return false;
    if (activeFilter === 'cycle' && !n.isInCycle) return false;
    if (activeFilter.startsWith('cluster:') && n.cluster !== activeFilter.slice(8)) return false;
    if (searchQuery) {
      const q = searchQuery.toLowerCase();
      const matchFile = n.relativePath.toLowerCase().includes(q);
      const matchSym = (n.symbols || []).some(s => s.toLowerCase().includes(q));
      if (!matchFile && !matchSym) return false;
    }
    return true;
  }

  let simTicks = 0;
  function stepPhysics() {
    if (simTicks > 240 && !draggedNode) return;
    simTicks++;
    const visible = nodes.filter(isNodeVisible);
    for (let i = 0; i < visible.length; i++) {
      const a = visible[i];
      for (let j = i + 1; j < visible.length; j++) {
        const b = visible[j];
        let dx = b.x - a.x, dy = b.y - a.y;
        let distSq = dx * dx + dy * dy + 1;
        if (distSq < 90000) {
          let f = 1400 / distSq;
          let dist = Math.sqrt(distSq);
          let fx = (dx / dist) * f, fy = (dy / dist) * f;
          a.vx -= fx; a.vy -= fy;
          b.vx += fx; b.vy += fy;
        }
      }
    }
    for (const e of edges) {
      const a = e.source, b = e.target;
      if (!isNodeVisible(a) || !isNodeVisible(b)) continue;
      let dx = b.x - a.x, dy = b.y - a.y;
      let dist = Math.sqrt(dx * dx + dy * dy) + 0.1;
      let f = (dist - 120) * 0.015;
      let fx = (dx / dist) * f, fy = (dy / dist) * f;
      a.vx += fx; a.vy += fy;
      b.vx -= fx; b.vy -= fy;
    }
    for (const n of visible) {
      if (n === draggedNode) continue;
      n.vx -= n.x * 0.004;
      n.vy -= n.y * 0.004;
      n.vx *= 0.82;
      n.vy *= 0.82;
      n.x += n.vx;
      n.y += n.vy;
    }
  }

  function draw() {
    stepPhysics();
    ctx.clearRect(0, 0, width, height);
    ctx.save();
    ctx.translate(width / 2 + camX, height / 2 + camY);
    ctx.scale(camScale, camScale);

    // Draw edges
    for (const e of edges) {
      if (!isNodeVisible(e.source) || !isNodeVisible(e.target)) continue;
      const isHighlighted = selectedNode && (e.source === selectedNode || e.target === selectedNode);
      ctx.beginPath();
      ctx.moveTo(e.source.x, e.source.y);
      ctx.lineTo(e.target.x, e.target.y);
      ctx.strokeStyle = isHighlighted
        ? (e.source === selectedNode ? '#38bdf8' : '#f59e0b')
        : 'rgba(148, 163, 184, 0.22)';
      ctx.lineWidth = isHighlighted ? 2 : 1;
      ctx.stroke();
    }

    // Draw nodes
    for (const n of nodes) {
      if (!isNodeVisible(n)) continue;
      ctx.beginPath();
      ctx.arc(n.x, n.y, n.r, 0, Math.PI * 2);

      let fill = clusterColorMap[n.cluster] || '#38bdf8';
      if (n.isInCycle) fill = '#ef4444';
      if (n.isModified) fill = '#10b981';
      if (n.isGodNode) fill = '#f59e0b';

      ctx.fillStyle = fill;
      ctx.fill();

      if (n === selectedNode || n === hoveredNode) {
        ctx.lineWidth = 3;
        ctx.strokeStyle = '#ffffff';
        ctx.stroke();
      }

      ctx.fillStyle = '#e2e8f0';
      ctx.font = (n.isGodNode ? 'bold 11px ' : '10px ') + 'sans-serif';
      const label = (n.isGodNode ? '★ ' : '') + n.fileName;
      ctx.fillText(label, n.x + n.r + 4, n.y + 3);
    }

    ctx.restore();
    requestAnimationFrame(draw);
  }

  function screenToWorld(sx, sy) {
    return {
      x: (sx - width / 2 - camX) / camScale,
      y: (sy - height / 2 - camY) / camScale
    };
  }

  function findNodeAt(wx, wy) {
    for (let i = nodes.length - 1; i >= 0; i--) {
      const n = nodes[i];
      if (!isNodeVisible(n)) continue;
      const dx = wx - n.x, dy = wy - n.y;
      if (dx * dx + dy * dy <= (n.r + 4) * (n.r + 4)) return n;
    }
    return null;
  }

  canvas.addEventListener('mousedown', (e) => {
    const rect = canvas.getBoundingClientRect();
    const w = screenToWorld(e.clientX - rect.left, e.clientY - rect.top);
    const hit = findNodeAt(w.x, w.y);
    if (hit) {
      draggedNode = hit;
      selectNode(hit);
    } else {
      isPanning = true;
      panStartX = e.clientX - camX;
      panStartY = e.clientY - camY;
    }
  });

  window.addEventListener('mousemove', (e) => {
    const rect = canvas.getBoundingClientRect();
    const w = screenToWorld(e.clientX - rect.left, e.clientY - rect.top);
    if (draggedNode) {
      draggedNode.x = w.x;
      draggedNode.y = w.y;
      simTicks = 180;
    } else if (isPanning) {
      camX = e.clientX - panStartX;
      camY = e.clientY - panStartY;
    } else {
      hoveredNode = findNodeAt(w.x, w.y);
    }
  });

  window.addEventListener('mouseup', () => {
    draggedNode = null;
    isPanning = false;
  });

  canvas.addEventListener('dblclick', (e) => {
    const rect = canvas.getBoundingClientRect();
    const w = screenToWorld(e.clientX - rect.left, e.clientY - rect.top);
    const hit = findNodeAt(w.x, w.y);
    if (hit) {
      vscode.postMessage({ command: 'openFile', relativePath: hit.relativePath });
    }
  });

  canvas.addEventListener('wheel', (e) => {
    e.preventDefault();
    const factor = e.deltaY < 0 ? 1.12 : 0.9;
    camScale = Math.max(0.2, Math.min(4, camScale * factor));
  }, { passive: false });

  function selectNode(n) {
    selectedNode = n;
    if (!n) return;
    const symsHtml = (n.symbols || []).length
      ? n.symbols.map(s => '<span class="tag">' + s + '</span>').join('')
      : '<span style="font-size:11px;color:#6b7280">No exported symbols</span>';

    const outHtml = (n.outgoingTargets || []).length
      ? n.outgoingTargets.map(t => '<span class="tag" data-Jump="' + t + '">→ ' + t.split('/').pop() + '</span>').join('')
      : '<span style="font-size:11px;color:#6b7280">None (Leaf)</span>';

    const inHtml = (n.incomingSources || []).length
      ? n.incomingSources.map(s => '<span class="tag" data-Jump="' + s + '">← ' + s.split('/').pop() + '</span>').join('')
      : '<span style="font-size:11px;color:#6b7280">None (Entrypoint)</span>';

    document.getElementById('inspectorBody').innerHTML =
      '<div class="node-title">' + (n.isGodNode ? '★ ' : '') + n.fileName + '</div>' +
      '<div class="node-path">' + n.relativePath + '</div>' +
      '<div class="stat-grid">' +
        '<div>Role: <b>' + n.role + '</b></div>' +
        '<div>PageRank: <b>' + n.pageRank + '</b></div>' +
        '<div>Used By (In): <b>' + n.inDegree + '</b></div>' +
        '<div>Depends On (Out): <b>' + n.outDegree + '</b></div>' +
        '<div>Language: <b>' + n.language + '</b></div>' +
        '<div>Lines: <b>' + n.lineCount + '</b></div>' +
      '</div>' +
      '<div style="margin-top:10px;font-size:11px;color:#9ca3af">Exported AST Symbols:</div>' +
      '<div class="tag-list">' + symsHtml + '</div>' +
      '<div style="margin-top:10px;font-size:11px;color:#9ca3af">Used By (Incoming Callers):</div>' +
      '<div class="tag-list">' + inHtml + '</div>' +
      '<div style="margin-top:10px;font-size:11px;color:#9ca3af">Depends On (Outgoing Imports):</div>' +
      '<div class="tag-list">' + outHtml + '</div>' +
      '<div class="btn-row">' +
        '<button class="btn" id="openSourceBtn">📂 Open File</button>' +
        '<button class="btn primary" id="genPromptBtn">✨ AI Prompt</button>' +
      '</div>';

    document.getElementById('openSourceBtn').onclick = () => {
      vscode.postMessage({ command: 'openFile', relativePath: n.relativePath });
    };
    document.getElementById('genPromptBtn').onclick = () => {
      vscode.postMessage({ command: 'generateFilePrompt', relativePath: n.relativePath });
    };
    document.querySelectorAll('[data-Jump]').forEach(el => {
      el.addEventListener('click', () => {
        const target = nodeById[el.getAttribute('data-Jump')];
        if (target) {
          selectNode(target);
          camX = -target.x * camScale;
          camY = -target.y * camScale;
        }
      });
    });
  }

  // Populate God Nodes & Cluster lists
  const godListEl = document.getElementById('godList');
  (data.godNodes || []).forEach(g => {
    const span = document.createElement('span');
    span.className = 'tag';
    span.style.borderColor = '#f59e0b';
    span.textContent = '★ ' + g.fileName + ' (' + g.pageRank + ')';
    span.onclick = () => {
      const target = nodeById[g.id];
      if (target) {
        selectNode(target);
        camX = -target.x * camScale;
        camY = -target.y * camScale;
      }
    };
    godListEl.appendChild(span);
  });

  const clusterListEl = document.getElementById('clusterList');
  (data.clusters || []).forEach(c => {
    const span = document.createElement('span');
    span.className = 'tag';
    span.textContent = c.name + ' (' + c.fileCount + ')';
    span.onclick = () => {
      document.getElementById('filterSelect').value = 'cluster:' + c.name;
      activeFilter = 'cluster:' + c.name;
      simTicks = 120;
    };
    clusterListEl.appendChild(span);
  });

  document.getElementById('searchInput').addEventListener('input', (e) => {
    searchQuery = e.target.value.trim();
    simTicks = 150;
  });
  document.getElementById('filterSelect').addEventListener('change', (e) => {
    activeFilter = e.target.value;
    simTicks = 150;
  });
  document.getElementById('resetViewBtn').onclick = () => {
    camX = 0; camY = 0; camScale = 1;
    activeFilter = 'all';
    document.getElementById('filterSelect').value = 'all';
  };
  document.getElementById('openMdBtn').onclick = () => {
    vscode.postMessage({ command: 'openMarkdown' });
  };
  document.getElementById('copyMdBtn').onclick = () => {
    vscode.postMessage({ command: 'copyMarkdown' });
  };

  const initFocus = ${JSON.stringify(initialFocusFile || '')};
  if (initFocus) {
    document.getElementById('searchInput').value = initFocus;
    searchQuery = initFocus.trim();
    const targetMatch = (data.nodes || []).find(n => n.fileName.toLowerCase() === initFocus.toLowerCase() || n.relativePath.toLowerCase().endsWith(initFocus.toLowerCase()));
    if (targetMatch) {
      selectedNode = targetMatch;
      showInspector(targetMatch);
    }
    simTicks = 200;
  } else if (selectedNode) {
    selectNode(selectedNode);
  }

  requestAnimationFrame(draw);
</script>
</body>
</html>`;
}

