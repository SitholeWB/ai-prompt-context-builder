#!/usr/bin/env node
const fs = require('fs');
const path = require('path');
const readline = require('readline');

// Dynamically resolve typescript if available
let ts = null;
try {
  ts = require('typescript');
} catch {}

if (!ts) {
  const candidatePaths = [
    path.join(__dirname, '../node_modules/typescript'),
    path.join(__dirname, '../../../../node_modules/typescript'),
    path.join(process.cwd(), 'node_modules/typescript'),
  ];
  if (process.env.NODE_PATH) {
    candidatePaths.push(path.join(process.env.NODE_PATH, 'typescript'));
  }
  const homeDir = process.env.HOME || process.env.USERPROFILE;
  if (homeDir) {
    candidatePaths.push(path.join(homeDir, '.local/share/antigravity-ide/resources/app/extensions/node_modules/typescript'));
  }
  for (const c of candidatePaths) {
    try {
      ts = require(c);
      if (ts) break;
    } catch {}
  }
}

const rl = readline.createInterface({
  input: process.stdin,
  output: process.stdout,
  terminal: false,
});

rl.on('line', (line) => {
  if (!line || !line.trim()) return;
  try {
    const request = JSON.parse(line);
    const response = handleRequest(request);
    process.stdout.write(JSON.stringify(response) + '\n');
  } catch (err) {
    const errResponse = {
      protocolVersion: '1.0',
      requestId: 'unknown',
      success: false,
      errorCode: 'WorkerProtocolError',
      errorMessage: err.message || String(err),
    };
    process.stdout.write(JSON.stringify(errResponse) + '\n');
  }
});

function handleRequest(req) {
  if (req.operation === 'ping') {
    return {
      protocolVersion: '1.0',
      requestId: req.requestId,
      success: true,
      adapter: { id: 'node', version: '1.0.0', capability: 'Semantic' },
    };
  }

  if (req.operation === 'analyse') {
    return analyze(req);
  }

  return {
    protocolVersion: '1.0',
    requestId: req.requestId,
    success: false,
    errorMessage: `Unsupported operation: ${req.operation}`,
  };
}

function analyze(req) {
  const rootPath = path.resolve(req.rootPath);
  const workspaceRoot = path.resolve(req.workspacePath || path.dirname(rootPath));
  const nodes = [];
  const edges = [];
  const diagnostics = [];
  const warnings = [];

  const visitedFiles = new Set();
  const queue = [rootPath];
  visitedFiles.add(rootPath);

  while (queue.length > 0) {
    const currentFile = queue.shift();
    if (!fs.existsSync(currentFile)) {
      continue;
    }

    const relPath = path.relative(workspaceRoot, currentFile).replace(/\\/g, '/');
    const ext = path.extname(currentFile).toLowerCase();
    const content = fs.readFileSync(currentFile, 'utf8');

    const fileNodeId = `file:${relPath}`;
    let lang = 'JavaScript';
    if (ext === '.ts' || ext === '.mts' || ext === '.cts') lang = 'TypeScript';
    else if (ext === '.tsx') lang = 'TSX';
    else if (ext === '.jsx') lang = 'JSX';
    else if (ext === '.html' || ext === '.htm') lang = 'HTML';
    else if (ext === '.css') lang = 'CSS';
    else if (ext === '.scss') lang = 'SCSS';
    else if (ext === '.sass') lang = 'Sass';
    else if (ext === '.vue') lang = 'Vue';
    else if (ext === '.json') lang = 'JSON';
    else if (ext === '.yaml' || ext === '.yml') lang = 'YAML';

    const fileNode = {
      id: fileNodeId,
      kind: 'File',
      language: lang,
      analysisLevel: ts ? 'Semantic' : 'SyntaxAware',
      displayName: path.basename(currentFile),
      qualifiedName: relPath,
      relativePath: relPath,
      sourceAvailable: true,
      content: content,
      diagnostics: [],
      metadata: {},
    };
    nodes.push(fileNode);

    if (ext === '.vue') {
      analyzeVueFile(currentFile, relPath, content, fileNodeId, nodes, edges, queue, visitedFiles, workspaceRoot);
    } else if (ext === '.html' || ext === '.htm') {
      analyzeHtmlFile(currentFile, relPath, content, fileNodeId, nodes, edges, queue, visitedFiles, workspaceRoot);
    } else if (ext === '.css' || ext === '.scss' || ext === '.sass') {
      analyzeStyleFile(currentFile, relPath, content, fileNodeId, nodes, edges, queue, visitedFiles, workspaceRoot);
    } else {
      // JS / TS / JSX / TSX
      analyzeJsTsFile(currentFile, relPath, content, fileNodeId, nodes, edges, queue, visitedFiles, workspaceRoot);
    }
  }

  return {
    protocolVersion: '1.0',
    requestId: req.requestId,
    success: true,
    adapter: { id: 'node', version: '1.0.0', capability: ts ? 'Semantic' : 'SyntaxAware' },
    graph: { nodes, edges, diagnostics, warnings },
  };
}

function analyzeJsTsFile(filePath, relPath, content, fileNodeId, nodes, edges, queue, visitedFiles, workspaceRoot) {
  // Use TypeScript Compiler API if available
  if (ts) {
    try {
      const sourceFile = ts.createSourceFile(
        filePath,
        content,
        ts.ScriptTarget.Latest,
        /* setParentNodes */ true,
        filePath.endsWith('.tsx') || filePath.endsWith('.jsx') ? ts.ScriptKind.TSX : ts.ScriptKind.TS
      );

      function visit(node) {
        // Discovered classes, functions, interfaces
        if (ts.isClassDeclaration(node) && node.name) {
          const className = node.name.text;
          const classNodeId = `type:${relPath}#${className}`;
          nodes.push({
            id: classNodeId,
            kind: 'Class',
            language: 'TypeScript',
            analysisLevel: 'Semantic',
            displayName: className,
            qualifiedName: className,
            relativePath: relPath,
            sourceAvailable: true,
            diagnostics: [],
            metadata: {},
          });
          edges.push({
            sourceNodeId: fileNodeId,
            targetNodeId: classNodeId,
            relationship: 'Export',
            confidence: 'Verified',
            analysisLevel: 'Semantic',
            metadata: {},
          });

          // Check Angular @Component decorators
          if (node.modifiers) {
            for (const mod of node.modifiers) {
              if (ts.isDecorator(mod)) {
                const decText = mod.getText(sourceFile);
                if (decText.includes('@Component')) {
                  // Angular template and style extraction
                  const tmplMatch = decText.match(/templateUrl\s*:\s*['"]([^'"]+)['"]/);
                  if (tmplMatch) {
                    const tmplPath = path.resolve(path.dirname(filePath), tmplMatch[1]);
                    linkAndQueueFile(fileNodeId, tmplPath, 'Template', 'Verified', 'CompanionFile', edges, queue, visitedFiles, workspaceRoot);
                  }
                  const styleMatches = decText.matchAll(/['"]([^'"]+\.(?:css|scss|sass))['"]/g);
                  for (const sm of styleMatches) {
                    const stylePath = path.resolve(path.dirname(filePath), sm[1]);
                    linkAndQueueFile(fileNodeId, stylePath, 'Style', 'Verified', 'CompanionFile', edges, queue, visitedFiles, workspaceRoot);
                  }
                }
              }
            }
          }
        }

        // Static imports: import ... from './...'
        if (ts.isImportDeclaration(node)) {
          const specifier = node.moduleSpecifier;
          if (ts.isStringLiteral(specifier)) {
            const importVal = specifier.text;
            if (isLocalImport(importVal)) {
              const target = resolveLocalImport(filePath, importVal);
              if (target) {
                const isStyle = /\.(css|scss|sass)$/i.test(target);
                const rel = isStyle ? 'StyleReference' : 'Import';
                linkAndQueueFile(fileNodeId, target, rel, 'Verified', 'Semantic', edges, queue, visitedFiles, workspaceRoot);
              }
            }
          }
        }

        // Dynamic imports: import('./...')
        if (ts.isCallExpression(node) && node.expression.kind === ts.SyntaxKind.ImportKeyword) {
          const arg = node.arguments[0];
          if (arg && ts.isStringLiteral(arg)) {
            const target = resolveLocalImport(filePath, arg.text);
            if (target) {
              linkAndQueueFile(fileNodeId, target, 'DynamicImport', 'Verified', 'Semantic', edges, queue, visitedFiles, workspaceRoot);
            }
          }
        }

        // CommonJS require('./...')
        if (ts.isCallExpression(node) && node.expression.getText(sourceFile) === 'require') {
          const arg = node.arguments[0];
          if (arg && ts.isStringLiteral(arg)) {
            const target = resolveLocalImport(filePath, arg.text);
            if (target) {
              linkAndQueueFile(fileNodeId, target, 'Import', 'Verified', 'Semantic', edges, queue, visitedFiles, workspaceRoot);
            }
          }
        }

        // React JSX Component Reference: <CustomerDetail />
        if (ts.isJsxOpeningElement(node) || ts.isJsxSelfClosingElement(node)) {
          const tag = node.tagName.getText(sourceFile);
          if (/^[A-Z]/.test(tag)) {
            // Find imported file with same name
            const siblingTsx = path.resolve(path.dirname(filePath), `${tag}.tsx`);
            const siblingTs = path.resolve(path.dirname(filePath), `${tag}.ts`);
            if (fs.existsSync(siblingTsx)) {
              linkAndQueueFile(fileNodeId, siblingTsx, 'ComponentUsage', 'High', 'SyntaxAware', edges, queue, visitedFiles, workspaceRoot);
            } else if (fs.existsSync(siblingTs)) {
              linkAndQueueFile(fileNodeId, siblingTs, 'ComponentUsage', 'High', 'SyntaxAware', edges, queue, visitedFiles, workspaceRoot);
            }
          }
        }

        ts.forEachChild(node, visit);
      }

      visit(sourceFile);
      return;
    } catch {}
  }

  // Regex fallback if TS AST fails
  fallbackJsImportScan(filePath, content, fileNodeId, edges, queue, visitedFiles, workspaceRoot);
}

function fallbackJsImportScan(filePath, content, fileNodeId, edges, queue, visitedFiles, workspaceRoot) {
  const importRegex = /(?:import|from)\s+['"]([^'"]+)['"]/g;
  let match;
  while ((match = importRegex.exec(content)) !== null) {
    const importPath = match[1];
    if (isLocalImport(importPath)) {
      const target = resolveLocalImport(filePath, importPath);
      if (target) {
        linkAndQueueFile(fileNodeId, target, 'Import', 'High', 'ImportGraph', edges, queue, visitedFiles, workspaceRoot);
      }
    }
  }

  // Fallback for Angular @Component: templateUrl & styleUrls
  const tmplMatch = /templateUrl\s*:\s*['"]([^'"]+)['"]/i.exec(content);
  if (tmplMatch) {
    const tmplFile = path.resolve(path.dirname(filePath), tmplMatch[1]);
    if (fs.existsSync(tmplFile)) {
      linkAndQueueFile(fileNodeId, tmplFile, 'Template', 'Verified', 'SyntaxAware', edges, queue, visitedFiles, workspaceRoot);
    }
  }

  const styleUrlsMatch = /styleUrls\s*:\s*\[([\s\S]*?)\]/i.exec(content);
  if (styleUrlsMatch) {
    const styleRegex = /['"]([^'"]+\.(?:css|scss|sass))['"]/g;
    let sMatch;
    while ((sMatch = styleRegex.exec(styleUrlsMatch[1])) !== null) {
      const styleFile = path.resolve(path.dirname(filePath), sMatch[1]);
      if (fs.existsSync(styleFile)) {
        linkAndQueueFile(fileNodeId, styleFile, 'Style', 'Verified', 'SyntaxAware', edges, queue, visitedFiles, workspaceRoot);
      }
    }
  }

  // Fallback for React JSX tags: <WidgetName ...
  const jsxTagRegex = /<([A-Z][A-Za-z0-9_]*)/g;
  let jMatch;
  while ((jMatch = jsxTagRegex.exec(content)) !== null) {
    const tag = jMatch[1];
    const siblingTsx = path.resolve(path.dirname(filePath), `${tag}.tsx`);
    const siblingTs = path.resolve(path.dirname(filePath), `${tag}.ts`);
    const siblingJsx = path.resolve(path.dirname(filePath), `${tag}.jsx`);
    if (fs.existsSync(siblingTsx)) {
      linkAndQueueFile(fileNodeId, siblingTsx, 'ComponentUsage', 'High', 'SyntaxAware', edges, queue, visitedFiles, workspaceRoot);
    } else if (fs.existsSync(siblingTs)) {
      linkAndQueueFile(fileNodeId, siblingTs, 'ComponentUsage', 'High', 'SyntaxAware', edges, queue, visitedFiles, workspaceRoot);
    } else if (fs.existsSync(siblingJsx)) {
      linkAndQueueFile(fileNodeId, siblingJsx, 'ComponentUsage', 'High', 'SyntaxAware', edges, queue, visitedFiles, workspaceRoot);
    }
  }
}

function analyzeVueFile(filePath, relPath, content, fileNodeId, nodes, edges, queue, visitedFiles, workspaceRoot) {
  // Script imports inside <script>
  const scriptMatch = content.match(/<script[\s\S]*?>([\s\S]*?)<\/script>/i);
  if (scriptMatch) {
    fallbackJsImportScan(filePath, scriptMatch[1], fileNodeId, edges, queue, visitedFiles, workspaceRoot);
  }

  // Style imports inside <style>
  const styleMatch = content.match(/<style[\s\S]*?>([\s\S]*?)<\/style>/i);
  if (styleMatch) {
    analyzeStyleContent(filePath, styleMatch[1], fileNodeId, edges, queue, visitedFiles, workspaceRoot);
  }
}

function analyzeHtmlFile(filePath, relPath, content, fileNodeId, nodes, edges, queue, visitedFiles, workspaceRoot) {
  // <script src="...">
  const scriptRegex = /<script\s+[^>]*src=['"]([^'"]+)['"]/gi;
  let m;
  while ((m = scriptRegex.exec(content)) !== null) {
    const src = m[1];
    if (isLocalImport(src)) {
      const target = path.resolve(path.dirname(filePath), src);
      if (fs.existsSync(target)) {
        linkAndQueueFile(fileNodeId, target, 'ScriptReference', 'Verified', 'SyntaxAware', edges, queue, visitedFiles, workspaceRoot);
      }
    }
  }

  // <link rel="stylesheet" href="...">
  const linkRegex = /<link\s+[^>]*href=['"]([^'"]+)['"][^>]*>/gi;
  while ((m = linkRegex.exec(content)) !== null) {
    const tag = m[0];
    const href = m[1];
    if (/rel=['"]stylesheet['"]/i.test(tag) && isLocalImport(href)) {
      const target = path.resolve(path.dirname(filePath), href);
      if (fs.existsSync(target)) {
        linkAndQueueFile(fileNodeId, target, 'StyleReference', 'Verified', 'SyntaxAware', edges, queue, visitedFiles, workspaceRoot);
      }
    }
  }
}

function analyzeStyleFile(filePath, relPath, content, fileNodeId, nodes, edges, queue, visitedFiles, workspaceRoot) {
  analyzeStyleContent(filePath, content, fileNodeId, edges, queue, visitedFiles, workspaceRoot);
}

function analyzeStyleContent(filePath, content, fileNodeId, edges, queue, visitedFiles, workspaceRoot) {
  // @import, @use, @forward
  const scssRegex = /@(?:import|use|forward)\s+['"]([^'"]+)['"]/g;
  let m;
  while ((m = scssRegex.exec(content)) !== null) {
    const importPath = m[1];
    const target = resolveSassImport(filePath, importPath);
    if (target) {
      linkAndQueueFile(fileNodeId, target, 'StyleReference', 'Verified', 'SyntaxAware', edges, queue, visitedFiles, workspaceRoot);
    }
  }
}

function isLocalImport(importVal) {
  return importVal.startsWith('.') || importVal.startsWith('/');
}

function resolveLocalImport(sourceFile, importVal) {
  const dir = path.dirname(sourceFile);
  const candidate = path.resolve(dir, importVal);

  if (fs.existsSync(candidate) && fs.statSync(candidate).isFile()) {
    return candidate;
  }

  const extensions = ['.ts', '.tsx', '.js', '.jsx', '.json', '.vue', '.css', '.scss'];
  for (const ext of extensions) {
    const withExt = candidate + ext;
    if (fs.existsSync(withExt)) return withExt;
  }

  // Directory index check
  for (const ext of extensions) {
    const indexFile = path.join(candidate, `index${ext}`);
    if (fs.existsSync(indexFile)) return indexFile;
  }

  return null;
}

function resolveSassImport(sourceFile, importVal) {
  const dir = path.dirname(sourceFile);
  const base = path.basename(importVal);
  const relDir = path.dirname(importVal);
  const targetDir = path.resolve(dir, relDir);

  const candidates = [
    path.resolve(dir, importVal),
    path.join(targetDir, base + '.scss'),
    path.join(targetDir, '_' + base + '.scss'),
    path.join(targetDir, base + '.css'),
    path.join(targetDir, base, '_index.scss'),
    path.join(targetDir, base, 'index.scss'),
  ];

  for (const c of candidates) {
    if (fs.existsSync(c) && fs.statSync(c).isFile()) {
      return c;
    }
  }
  return null;
}

function linkAndQueueFile(sourceNodeId, targetFile, relationship, confidence, analysisLevel, edges, queue, visitedFiles, workspaceRoot) {
  const targetRel = path.relative(workspaceRoot, targetFile).replace(/\\/g, '/');
  const targetNodeId = `file:${targetRel}`;

  edges.push({
    sourceNodeId,
    targetNodeId,
    relationship,
    confidence,
    analysisLevel,
    metadata: {},
  });

  if (!visitedFiles.has(targetFile)) {
    visitedFiles.add(targetFile);
    queue.push(targetFile);
  }
}
