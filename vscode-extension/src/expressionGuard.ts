const BLOCKED_EF_PATTERNS: RegExp[] = [
  /\bSaveChanges(?:Async)?\s*\(/i,
  /\b(?:Add|AddRange|Update|UpdateRange|Remove|RemoveRange|Attach|AttachRange)\s*\(/i,
  /\bExecuteDelete(?:Async)?\s*\(/i,
  /\bExecuteUpdate(?:Async)?\s*\(/i,
  /\bExecuteSql(?:Raw)?(?:Async)?\s*\(/i,
  /\bDatabase\s*\.\s*(?:ExecuteSql(?:Raw)?|EnsureDeleted|EnsureCreated|Migrate)\s*\(/i,
  /\bFromSqlRaw\s*</i,
];

const BLOCKED_SQL_IN_STRING = /\b(?:DROP|DELETE|INSERT|UPDATE|TRUNCATE|ALTER|CREATE)\b/i;
const SCRIPT_REFERENCE_DIRECTIVE_PATTERN = /^\s*#(?:load|r)\b/im;

/**
 * Remove C# comments without treating `//` or `/*` inside string/char literals as comments.
 * Naive `//.*$` stripping turns URL filters like `"http://example.com").ExecuteDelete()` into
 * `"http:` and lets guarded UI paths execute the hidden mutation.
 */
function stripCSharpComments(expression: string): string {
  const src = expression;
  let i = 0;
  let out = '';

  const peek = (ahead = 0): string => src[i + ahead] ?? '';

  const skipLineComment = (): void => {
    i += 2;
    while (i < src.length && src[i] !== '\n' && src[i] !== '\r') {
      i++;
    }
  };

  const skipBlockComment = (): void => {
    i += 2;
    while (i < src.length && !(src[i] === '*' && src[i + 1] === '/')) {
      i++;
    }
    if (i < src.length) {
      i += 2;
    }
  };

  const readCharLiteral = (): void => {
    out += src[i];
    i++;
    if (i < src.length && src[i] === '\\') {
      out += src[i];
      i++;
      if (i < src.length) {
        out += src[i];
        i++;
      }
    } else if (i < src.length) {
      out += src[i];
      i++;
    }
    if (i < src.length && src[i] === "'") {
      out += src[i];
      i++;
    }
  };

  const readCode = (stopAtUnmatchedBrace: boolean): void => {
    let braceDepth = 0;

    while (i < src.length) {
      const c = peek();
      const n = peek(1);

      if (stopAtUnmatchedBrace && c === '}' && braceDepth === 0) {
        return;
      }

      if (c === '/' && n === '/') {
        skipLineComment();
        out += ' ';
        continue;
      }

      if (c === '/' && n === '*') {
        skipBlockComment();
        out += ' ';
        continue;
      }

      if (c === "'") {
        readCharLiteral();
        continue;
      }

      if (tryReadString()) {
        continue;
      }

      if (c === '{') {
        braceDepth++;
      } else if (c === '}' && braceDepth > 0) {
        braceDepth--;
      }

      out += c;
      i++;
    }
  };

  const readInterpolationHole = (): void => {
    out += '{';
    i++;
    readCode(true);
    if (peek() === '}') {
      out += '}';
      i++;
    }
  };

  const readQuotedString = (verbatim: boolean, interpolated: boolean): void => {
    out += '"';
    i++;

    while (i < src.length) {
      const c = peek();
      const n = peek(1);

      if (interpolated && c === '{') {
        if (n === '{') {
          out += '{{';
          i += 2;
          continue;
        }

        readInterpolationHole();
        continue;
      }

      if (interpolated && c === '}' && n === '}') {
        out += '}}';
        i += 2;
        continue;
      }

      if (verbatim) {
        if (c === '"' && n === '"') {
          out += '""';
          i += 2;
          continue;
        }

        if (c === '"') {
          out += '"';
          i++;
          return;
        }

        out += c;
        i++;
        continue;
      }

      if (c === '\\' && n) {
        out += c + n;
        i += 2;
        continue;
      }

      if (c === '"') {
        out += '"';
        i++;
        return;
      }

      out += c;
      i++;
    }
  };

  const readRawString = (quoteCount: number, dollars: number): void => {
    for (let copied = 0; copied < quoteCount; copied++) {
      out += '"';
      i++;
    }

    const holeOpen = dollars > 0 ? '{'.repeat(dollars) : '';

    while (i < src.length) {
      if (holeOpen && src.startsWith(holeOpen, i) && peek(dollars) !== '{') {
        for (let copied = 0; copied < dollars; copied++) {
          out += '{';
          i++;
        }

        readCode(true);

        let closed = 0;
        while (closed < dollars && peek() === '}') {
          out += '}';
          i++;
          closed++;
        }

        continue;
      }

      let quotes = 0;
      while (peek(quotes) === '"') {
        quotes++;
      }

      if (quotes >= quoteCount) {
        for (let copied = 0; copied < quoteCount; copied++) {
          out += '"';
          i++;
        }
        return;
      }

      out += peek();
      i++;
    }
  };

  const tryReadString = (): boolean => {
    let j = i;
    let dollars = 0;
    let verbatim = false;

    if (src[j] === '@' && src[j + 1] === '$') {
      verbatim = true;
      j += 2;
      dollars = 1;
      while (src[j] === '$') {
        dollars++;
        j++;
      }
    } else {
      while (src[j] === '$') {
        dollars++;
        j++;
      }

      if (src[j] === '@') {
        verbatim = true;
        j++;
      }
    }

    if (src[j] !== '"') {
      return false;
    }

    let quoteCount = 0;
    while (src[j + quoteCount] === '"') {
      quoteCount++;
    }

    while (i < j) {
      out += src[i];
      i++;
    }

    if (quoteCount >= 3) {
      readRawString(quoteCount, dollars);
      return true;
    }

    readQuotedString(verbatim, dollars > 0);
    return true;
  };

  readCode(false);
  return out;
}

function findBlockedSqlInStrings(expression: string): string | undefined {
  const stringPattern = /"(?:[^"\\]|\\.)*"|@"(?:[^"]|"")*"|'(?:[^'\\]|\\.)*'/g;
  let match: RegExpExecArray | null;

  while ((match = stringPattern.exec(expression)) !== null) {
    const literal = match[0];
    if (BLOCKED_SQL_IN_STRING.test(literal)) {
      return 'SQL literals cannot contain DROP, DELETE, INSERT, UPDATE, TRUNCATE, ALTER, or CREATE.';
    }
  }

  return undefined;
}

export interface ExpressionGuardResult {
  ok: boolean;
  reason?: string;
}

export function validateReadOnlyExpression(expression: string): ExpressionGuardResult {
  const trimmed = expression.trim();
  if (!trimmed) {
    return { ok: false, reason: 'Expression is empty.' };
  }

  const stripped = stripCSharpComments(trimmed);

  if (SCRIPT_REFERENCE_DIRECTIVE_PATTERN.test(stripped)) {
    return {
      ok: false,
      reason: 'Read-only mode: #load and #r directives are not allowed from guarded UI execution paths.',
    };
  }

  for (const pattern of BLOCKED_EF_PATTERNS) {
    if (pattern.test(stripped)) {
      return {
        ok: false,
        reason: 'Read-only mode: SaveChanges, Add/Update/Remove, ExecuteSql, ExecuteDelete/Update, and schema changes are not allowed from the result panel.',
      };
    }
  }

  const sqlInString = findBlockedSqlInStrings(stripped);
  if (sqlInString) {
    return { ok: false, reason: sqlInString };
  }

  if (BLOCKED_SQL_IN_STRING.test(stripped) && /\b(?:ExecuteSql|FromSqlRaw|SqlQuery)\b/i.test(stripped)) {
    return {
      ok: false,
      reason: 'Destructive SQL keywords are not allowed in ExecuteSql / FromSqlRaw expressions.',
    };
  }

  return { ok: true };
}
