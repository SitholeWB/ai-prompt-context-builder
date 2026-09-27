const fs = require('fs');
const path = require('path');

function getTs() {
  try { return require('typescript'); } catch {}
  const candidateDirs = [
    path.join(__dirname, '../../workers/node/node_modules/typescript'),
    path.join(process.cwd(), 'node_modules/typescript'),
    process.env.NODE_PATH ? path.join(process.env.NODE_PATH, 'typescript') : null,
  ];
  const homeDir = process.env.HOME || process.env.USERPROFILE;
  if (homeDir) {
    candidateDirs.push(path.join(homeDir, '.local/share/antigravity-ide/resources/app/extensions/node_modules/typescript'));
  }
  for (const c of candidateDirs) {
    if (c && fs.existsSync(c)) {
      try { return require(c); } catch {}
    }
  }
  throw new Error('TypeScript compiler not found.');
}

const ts = getTs();
const srcFile = path.resolve(__dirname, 'src/extension.ts');
const sourceText = fs.readFileSync(srcFile, 'utf8');

const result = ts.transpileModule(sourceText, {
  compilerOptions: {
    module: ts.ModuleKind.CommonJS,
    target: ts.ScriptTarget.ES2022,
    sourceMap: true,
  }
});

const outDir = path.resolve(__dirname, 'dist');
fs.mkdirSync(outDir, { recursive: true });
fs.writeFileSync(path.join(outDir, 'extension.js'), result.outputText, 'utf8');
if (result.sourceMapText) {
  fs.writeFileSync(path.join(outDir, 'extension.js.map'), result.sourceMapText, 'utf8');
}
console.log('✓ Successfully compiled extension to dist/extension.js');
