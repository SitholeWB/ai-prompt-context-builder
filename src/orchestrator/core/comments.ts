import { CommentMode, LanguageName } from '../protocol/types.js';

export interface CommentRemovalResult {
  code: string;
  commentsRemoved: boolean;
  supported: boolean;
}

/**
 * TypeScript / JavaScript Scanner-based comment removal
 */
export function removeTypeScriptComments(source: string): string {
  try {
    const ts = require('typescript');
    const scanner = ts.createScanner(
      ts.ScriptTarget.Latest,
      /* skipTrivia */ false,
      ts.LanguageVariant.Standard,
      source
    );

    let output = '';
    let token = scanner.scan();

    while (token !== ts.SyntaxKind.EndOfFileToken) {
      if (
        token === ts.SyntaxKind.SingleLineCommentTrivia ||
        token === ts.SyntaxKind.MultiLineCommentTrivia
      ) {
        // Skip comment tokens
      } else {
        output += scanner.getTokenText();
      }
      token = scanner.scan();
    }
    return output;
  } catch {
    return fallbackJsCommentRemoval(source);
  }
}

/**
 * Fallback tokenizer for JS/TS/Java/C#/Go when compiler API scanner isn't directly loaded
 */
function tokenizeCStyleComments(source: string, isScss = false): string {
  let result = '';
  let i = 0;
  const len = source.length;

  while (i < len) {
    const char = source[i];
    const next = i + 1 < len ? source[i + 1] : '';

    // Check for string literals (double quotes, single quotes, backticks)
    if (char === '"' || char === "'" || char === '`') {
      const quote = char;
      result += quote;
      i++;
      while (i < len) {
        const c = source[i];
        result += c;
        if (c === '\\' && i + 1 < len) {
          i++;
          result += source[i];
        } else if (c === quote) {
          i++;
          break;
        }
        i++;
      }
      continue;
    }

    // Check for single line comments //
    if (char === '/' && next === '/') {
      i += 2;
      while (i < len && source[i] !== '\n') {
        i++;
      }
      continue;
    }

    // Check for multi-line comments /* ... */
    if (char === '/' && next === '*') {
      i += 2;
      while (i < len && !(source[i] === '*' && i + 1 < len && source[i + 1] === '/')) {
        i++;
      }
      if (i < len) i += 2; // Skip */
      continue;
    }

    result += char;
    i++;
  }

  return result;
}

export function fallbackJsCommentRemoval(source: string): string {
  return tokenizeCStyleComments(source);
}

/**
 * C# and VB Comment Removal
 * Preserves preprocessor directives (#region, #if, #pragma), raw strings ("""..."""), verbatim strings (@"...").
 */
export function removeCSharpComments(source: string): string {
  let result = '';
  let i = 0;
  const len = source.length;

  while (i < len) {
    // Check for preprocessor directives at line start
    if (
      source[i] === '#' &&
      (i === 0 || source[i - 1] === '\n' || source.slice(Math.max(0, i - 10), i).trim() === '')
    ) {
      while (i < len && source[i] !== '\n') {
        result += source[i];
        i++;
      }
      continue;
    }

    // Verbatim string @"..." or $@""
    if (
      (source[i] === '@' && i + 1 < len && source[i + 1] === '"') ||
      (source[i] === '$' && i + 2 < len && source[i + 1] === '@' && source[i + 2] === '"')
    ) {
      if (source[i] === '$') {
        result += '$@"';
        i += 3;
      } else {
        result += '@"';
        i += 2;
      }
      while (i < len) {
        if (source[i] === '"' && i + 1 < len && source[i + 1] === '"') {
          result += '""';
          i += 2;
        } else if (source[i] === '"') {
          result += '"';
          i++;
          break;
        } else {
          result += source[i];
          i++;
        }
      }
      continue;
    }

    // Raw string literal """ ... """
    if (source.startsWith('"""', i)) {
      result += '"""';
      i += 3;
      while (i < len && !source.startsWith('"""', i)) {
        result += source[i];
        i++;
      }
      if (i < len) {
        result += '"""';
        i += 3;
      }
      continue;
    }

    // Regular strings
    if (source[i] === '"' || source[i] === "'") {
      const quote = source[i];
      result += quote;
      i++;
      while (i < len) {
        const c = source[i];
        result += c;
        if (c === '\\' && i + 1 < len) {
          i++;
          result += source[i];
        } else if (c === quote) {
          i++;
          break;
        }
        i++;
      }
      continue;
    }

    // Line comments //
    if (source[i] === '/' && i + 1 < len && source[i + 1] === '/') {
      i += 2;
      while (i < len && source[i] !== '\n') {
        i++;
      }
      continue;
    }

    // Block comments /* */
    if (source[i] === '/' && i + 1 < len && source[i + 1] === '*') {
      i += 2;
      while (i < len && !(source[i] === '*' && i + 1 < len && source[i + 1] === '/')) {
        i++;
      }
      if (i < len) i += 2;
      continue;
    }

    result += source[i];
    i++;
  }

  return result;
}

export function removeVbComments(source: string): string {
  let result = '';
  let inString = false;
  for (let i = 0; i < source.length; i++) {
    const char = source[i];
    if (char === '"') {
      inString = !inString;
      result += char;
      continue;
    }
    if (!inString && (char === "'" || (char === 'R' && source.slice(i, i + 4).toUpperCase() === 'REM '))) {
      while (i < source.length && source[i] !== '\n') {
        i++;
      }
      if (i < source.length) result += '\n';
      continue;
    }
    result += char;
  }
  return result;
}

/**
 * Python Comment Removal
 * Preserves docstrings (both """ and '''), strings, and shebang lines.
 * Removes only true comments (#).
 */
export function removePythonComments(source: string): string {
  let result = '';
  let i = 0;
  const len = source.length;

  while (i < len) {
    // Preserve shebang on first line
    if (i === 0 && source.startsWith('#!')) {
      while (i < len && source[i] !== '\n') {
        result += source[i];
        i++;
      }
      continue;
    }

    // Triple-quoted docstrings / multiline strings
    if (source.startsWith('"""', i) || source.startsWith("'''", i)) {
      const triple = source.slice(i, i + 3);
      result += triple;
      i += 3;
      while (i < len && !source.startsWith(triple, i)) {
        if (source[i] === '\\' && i + 1 < len) {
          result += source[i] + source[i + 1];
          i += 2;
        } else {
          result += source[i];
          i++;
        }
      }
      if (i < len) {
        result += triple;
        i += 3;
      }
      continue;
    }

    // Single-line strings
    if (source[i] === '"' || source[i] === "'") {
      const quote = source[i];
      result += quote;
      i++;
      while (i < len) {
        const c = source[i];
        result += c;
        if (c === '\\' && i + 1 < len) {
          i++;
          result += source[i];
        } else if (c === quote) {
          i++;
          break;
        }
        i++;
      }
      continue;
    }

    // Comment #
    if (source[i] === '#') {
      while (i < len && source[i] !== '\n') {
        i++;
      }
      continue;
    }

    result += source[i];
    i++;
  }

  return result;
}

/**
 * HTML comment removal: removes <!-- ... -->
 */
export function removeHtmlComments(source: string): string {
  return source.replace(/<!--[\s\S]*?-->/g, '');
}

/**
 * CSS / SCSS comment removal
 */
export function removeCssComments(source: string, isScss = false): string {
  return tokenizeCStyleComments(source, isScss);
}

/**
 * Razor comment removal: removes @* ... *@ and delegating HTML/C#
 */
export function removeRazorComments(source: string): string {
  const withoutRazorComments = source.replace(/@\*[\s\S]*?\*@/g, '');
  return removeHtmlComments(withoutRazorComments);
}

/**
 * Vue SFC comment removal: removes comments per block
 */
export function removeVueComments(source: string): string {
  return source.replace(/(<template[\s\S]*?>)([\s\S]*?)(<\/template>)/gi, (_, open, content, close) => {
    return open + removeHtmlComments(content) + close;
  }).replace(/(<script[\s\S]*?>)([\s\S]*?)(<\/script>)/gi, (_, open, content, close) => {
    return open + removeTypeScriptComments(content) + close;
  }).replace(/(<style[\s\S]*?>)([\s\S]*?)(<\/style>)/gi, (_, open, content, close) => {
    return open + removeCssComments(content, true) + close;
  });
}

/**
 * Main dispatcher for comment removal based on language or file extension
 */
export function removeComments(source: string, extension: string, language?: LanguageName): CommentRemovalResult {
  const ext = extension.toLowerCase();

  switch (ext) {
    case '.ts':
    case '.tsx':
    case '.js':
    case '.jsx':
    case '.mjs':
    case '.cjs':
    case '.mts':
    case '.cts':
      return { code: removeTypeScriptComments(source), commentsRemoved: true, supported: true };

    case '.cs':
      return { code: removeCSharpComments(source), commentsRemoved: true, supported: true };

    case '.vb':
      return { code: removeVbComments(source), commentsRemoved: true, supported: true };

    case '.py':
      return { code: removePythonComments(source), commentsRemoved: true, supported: true };

    case '.html':
    case '.htm':
      return { code: removeHtmlComments(source), commentsRemoved: true, supported: true };

    case '.css':
      return { code: removeCssComments(source, false), commentsRemoved: true, supported: true };

    case '.scss':
    case '.sass':
      return { code: removeCssComments(source, true), commentsRemoved: true, supported: true };

    case '.vue':
      return { code: removeVueComments(source), commentsRemoved: true, supported: true };

    case '.razor':
    case '.cshtml':
      return { code: removeRazorComments(source), commentsRemoved: true, supported: true };

    case '.java':
    case '.go':
      return { code: tokenizeCStyleComments(source), commentsRemoved: true, supported: true };

    case '.json':
    case '.yaml':
    case '.yml':
      // JSON does not have comments in strict spec; YAML comments are lines starting with #
      if (ext === '.yaml' || ext === '.yml') {
        return { code: removePythonComments(source), commentsRemoved: true, supported: true };
      }
      return { code: source, commentsRemoved: false, supported: true };

    default:
      return { code: source, commentsRemoved: false, supported: false };
  }
}
