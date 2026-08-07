/** Optional C# generic type-argument list before `(`, e.g. ExecuteDeleteAsync<Product>(...). */
const OPTIONAL_TYPE_ARGS = '(?:\\s*<[^>(]+>)?';

const BLOCKED_EF_PATTERNS: RegExp[] = [
  new RegExp(`\\bSaveChanges(?:Async)?${OPTIONAL_TYPE_ARGS}\\s*\\(`, 'i'),
  /\b(?:Add|AddRange|Update|UpdateRange|Remove|RemoveRange|Attach|AttachRange)\s*\(/i,
  // Allow ExecuteDeleteAsync<T>(...) / ExecuteUpdateAsync<T>(...) static/generic call shapes.
  new RegExp(`\\bExecuteDelete(?:Async)?${OPTIONAL_TYPE_ARGS}\\s*\\(`, 'i'),
  new RegExp(`\\bExecuteUpdate(?:Async)?${OPTIONAL_TYPE_ARGS}\\s*\\(`, 'i'),
  new RegExp(`\\bExecuteSql(?:Raw|Interpolated)?(?:Async)?${OPTIONAL_TYPE_ARGS}\\s*\\(`, 'i'),
  // Standalone schema APIs catch Database.GetService<...>().EnsureDeleted() indirection.
  new RegExp(`\\b(?:EnsureDeleted|EnsureCreated|Migrate)(?:Async)?${OPTIONAL_TYPE_ARGS}\\s*\\(`, 'i'),
  /\bDatabase\s*\.\s*(?:ExecuteSql(?:Raw|Interpolated)?(?:Async)?|EnsureDeleted(?:Async)?|EnsureCreated(?:Async)?|Migrate(?:Async)?)\s*\(/i,
  // IRelationalDatabaseCreator.Delete() via GetService<...>().Delete()
  /\bGetService\s*<[^>]*>\s*\(\s*\)\s*\.\s*Delete\s*\(/i,
  /\bFromSqlRaw\s*</i,
];

const BLOCKED_SQL_IN_STRING = /\b(?:DROP|DELETE|INSERT|UPDATE|TRUNCATE|ALTER|CREATE)\b/i;
const SCRIPT_REFERENCE_DIRECTIVE_PATTERN = /^\s*#(?:load|r)\b/im;

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
