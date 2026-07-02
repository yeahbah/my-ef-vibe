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
const LOAD_DIRECTIVE_PATTERN = /^\s*#load\b/im;

function stripCSharpComments(expression: string): string {
  let result = expression.replace(/\/\*[\s\S]*?\*\//g, ' ');
  result = result.replace(/\/\/.*$/gm, ' ');
  return result;
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

  if (LOAD_DIRECTIVE_PATTERN.test(stripped)) {
    return {
      ok: false,
      reason: 'Read-only mode: #load directives are not allowed from guarded UI execution paths.',
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
