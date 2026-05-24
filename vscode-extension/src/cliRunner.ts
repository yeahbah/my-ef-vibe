import { execFile } from 'child_process';
import { promisify } from 'util';
import * as fs from 'fs';
import * as path from 'path';
import type { EfvibeSettings } from './config';
import { parseEvaluationJson, type EvaluationJsonPayload } from './evaluationTypes';

const execFileAsync = promisify(execFile);

export function findDotnetToolsManifest(startDirectory: string): string | undefined {
  let current = path.resolve(startDirectory);

  for (let depth = 0; depth < 12; depth++) {
    const candidate = path.join(current, 'dotnet-tools.json');
    if (fs.existsSync(candidate)) {
      return candidate;
    }

    const parent = path.dirname(current);
    if (parent === current) {
      break;
    }

    current = parent;
  }

  return undefined;
}

export interface ToolInvocation {
  kind: 'path' | 'dotnet-tool' | 'global';
  command: string;
  prefixArgs: string[];
  framework?: string;
}

export interface AboutJsonPayload {
  toolVersion: string;
  command: string;
  productName: string;
  description: string;
  author: string;
  license: string;
  website: string;
  repository: string;
  nuGet: string;
  runtime: string;
}

export interface TablesJsonEntry {
  dbSet: string;
  entityType: string;
  entityTypeFullName?: string;
}

export interface DescribeJsonMember {
  name: string;
  type: string;
  nullable: string;
  notes?: string;
}

export interface DescribeJsonPayload {
  success: boolean;
  dbSet?: string;
  entityType?: string;
  entityTypeFullName?: string;
  members?: DescribeJsonMember[];
  error?: string;
  knownEntities?: string[];
}

export interface DbInfoJsonEntry {
  key: string;
  value?: string;
}

export interface DbInfoJsonPayload {
  dbContext: string;
  entries: DbInfoJsonEntry[];
}

export interface CompletionJsonItem {
  label: string;
  insertText: string;
  kind: string;
  detail?: string;
}

export interface CompletionsJsonPayload {
  prefix: string;
  items: CompletionJsonItem[];
}

export interface TablesJsonPayload {
  dbContext: string;
  tables: TablesJsonEntry[];
}

function quoteArg(value: string): string {
  if (value.length === 0) {
    return '""';
  }

  if (!/[ \t"]/u.test(value)) {
    return value;
  }

  return `"${value.replace(/"/g, '\\"')}"`;
}

export function resolveToolInvocation(
  workspaceRoot: string,
  toolPath: string,
  dotnetFramework?: string,
): ToolInvocation {
  if (toolPath.trim()) {
    return {
      kind: 'path',
      command: toolPath.trim(),
      prefixArgs: [],
    };
  }

  const manifestPath = findDotnetToolsManifest(workspaceRoot);
  if (manifestPath) {
    try {
      const manifest = JSON.parse(fs.readFileSync(manifestPath, 'utf8')) as {
        tools?: Record<string, unknown>;
      };

      if (manifest.tools && 'efvibe' in manifest.tools) {
        const framework = dotnetFramework?.trim() || undefined;
        const prefixArgs = framework ? ['efvibe', '-f', framework] : ['efvibe'];
        return {
          kind: 'dotnet-tool',
          command: 'dotnet',
          prefixArgs,
          framework,
        };
      }
    } catch {
      // Fall through to global efvibe on PATH.
    }
  }

  return {
    kind: 'global',
    command: 'efvibe',
    prefixArgs: [],
  };
}

export function buildServeArgs(settings: EfvibeSettings): string[] {
  return ['serve', ...buildEfvibeArgs(settings)];
}

export function buildEfvibeArgs(settings: EfvibeSettings): string[] {
  const args: string[] = [];

  if (settings.workspaceRoot) {
    args.push('-w', settings.workspaceRoot);
  }

  if (settings.project) {
    args.push('-p', settings.project);
  }

  if (settings.startupProject && fs.existsSync(settings.startupProject)) {
    args.push('-s', settings.startupProject);
  }

  if (settings.context) {
    args.push('-c', settings.context);
  }

  if (settings.connectionString) {
    args.push('--connection-string', settings.connectionString);
  }

  if (settings.provider) {
    args.push('--provider', settings.provider);
  }

  if (!settings.dbLog) {
    args.push('--no-dblog');
  }

  if (settings.dotnetFramework) {
    args.push('--framework', settings.dotnetFramework);
  }

  return args;
}

export function buildCommandLine(invocation: ToolInvocation, args: string[]): string {
  const tokens = [...invocation.prefixArgs, ...args];
  return [quoteArg(invocation.command), ...tokens.map(quoteArg)].join(' ');
}

export function buildReplCommand(settings: EfvibeSettings, searchDirectory: string): string {
  const invocation = resolveToolInvocation(
    searchDirectory,
    settings.toolPath,
    settings.dotnetFramework,
  );
  return buildCommandLine(invocation, buildEfvibeArgs(settings));
}

export interface ExpressionRunOptions {
  format?: 'text' | 'json';
  noBanner?: boolean;
  withPlan?: boolean;
}

export function buildExpressionArgs(
  settings: EfvibeSettings,
  expression: string,
  options: ExpressionRunOptions = {},
): string[] {
  const args = [...buildEfvibeArgs(settings), '-e', expression];

  if (options.format === 'json') {
    args.push('--format', 'json', '--no-banner');
  } else if (options.noBanner) {
    args.push('--no-banner');
  }

  if (options.withPlan) {
    args.push('--with-plan');
  }

  return args;
}

export function buildExpressionCommand(
  settings: EfvibeSettings,
  searchDirectory: string,
  expression: string,
  options: ExpressionRunOptions = {},
): string {
  const invocation = resolveToolInvocation(
    searchDirectory,
    settings.toolPath,
    settings.dotnetFramework,
  );
  return buildCommandLine(invocation, buildExpressionArgs(settings, expression, options));
}

export interface ExpressionRunResult {
  exitCode: number;
  stdout: string;
  stderr: string;
  payload?: EvaluationJsonPayload;
}

export async function runExpressionJson(
  settings: EfvibeSettings,
  searchDirectory: string,
  cwd: string,
  expression: string,
  options?: Pick<ExpressionRunOptions, 'withPlan'> & { preferDaemon?: boolean },
): Promise<ExpressionRunResult> {
  if (options?.preferDaemon !== false) {
    try {
      const { runExpressionViaDaemon } = await import('./daemonClient');
      return await runExpressionViaDaemon(
        settings,
        searchDirectory,
        cwd,
        expression,
        options?.withPlan ?? false,
      );
    } catch {
      // Fall back to one-shot when serve is unavailable or the daemon crashed.
    }
  }

  const invocation = resolveToolInvocation(
    searchDirectory,
    settings.toolPath,
    settings.dotnetFramework,
  );
  const args = buildExpressionArgs(settings, expression, {
    format: 'json',
    withPlan: options?.withPlan,
  });

  try {
    const { stdout, stderr } = await execFileAsync(invocation.command, [...invocation.prefixArgs, ...args], {
      cwd,
      timeout: 10 * 60_000,
      maxBuffer: 8 * 1024 * 1024,
      windowsHide: true,
    });

    return {
      exitCode: 0,
      stdout,
      stderr,
      payload: parseEvaluationJson(stdout),
    };
  } catch (error) {
    const execError = error as NodeJS.ErrnoException & {
      stdout?: string;
      stderr?: string;
      code?: number;
    };

    const stdout = execError.stdout ?? '';
    const stderr = execError.stderr ?? '';
    const payload = parseEvaluationJson(stdout);

    return {
      exitCode: typeof execError.code === 'number' ? execError.code : 1,
      stdout,
      stderr,
      payload,
    };
  }
}

export function buildAboutJsonCommand(
  searchDirectory: string,
  toolPath?: string,
  dotnetFramework?: string,
): string {
  const invocation = resolveToolInvocation(searchDirectory, toolPath ?? '', dotnetFramework);
  return buildCommandLine(invocation, ['--about-json', '--no-banner']);
}

async function runJsonStdout<T>(
  settings: EfvibeSettings,
  searchDirectory: string,
  cwd: string,
  flag: string,
  options?: { timeoutMs?: number },
): Promise<T | undefined> {
  const invocation = resolveToolInvocation(
    searchDirectory,
    settings.toolPath,
    settings.dotnetFramework,
  );
  const args = [...buildEfvibeArgs(settings), flag, '--no-banner'];

  try {
    const { stdout } = await execFileAsync(invocation.command, [...invocation.prefixArgs, ...args], {
      cwd,
      timeout: options?.timeoutMs ?? 10 * 60_000,
      maxBuffer: 4 * 1024 * 1024,
      windowsHide: true,
    });

    const line = stdout
      .split(/\r?\n/u)
      .map((entry) => entry.trim())
      .find((entry) => entry.startsWith('{'));

    if (!line) {
      return undefined;
    }

    return JSON.parse(line) as T;
  } catch {
    return undefined;
  }
}

export async function runAboutJson(
  searchDirectory: string,
  cwd: string,
  options?: { toolPath?: string; dotnetFramework?: string },
): Promise<AboutJsonPayload | undefined> {
  const invocation = resolveToolInvocation(
    searchDirectory,
    options?.toolPath ?? '',
    options?.dotnetFramework,
  );
  const args = ['--about-json', '--no-banner'];

  try {
    const { stdout } = await execFileAsync(invocation.command, [...invocation.prefixArgs, ...args], {
      cwd,
      timeout: 15_000,
      maxBuffer: 1024 * 1024,
      windowsHide: true,
    });

    const line = stdout
      .split(/\r?\n/u)
      .map((entry) => entry.trim())
      .find((entry) => entry.startsWith('{'));

    if (!line) {
      return undefined;
    }

    return JSON.parse(line) as AboutJsonPayload;
  } catch {
    return undefined;
  }
}

export async function runTablesJson(
  settings: EfvibeSettings,
  searchDirectory: string,
  cwd: string,
): Promise<TablesJsonPayload | undefined> {
  return runJsonStdout<TablesJsonPayload>(settings, searchDirectory, cwd, '--tables-json');
}

export async function runDescribeJson(
  settings: EfvibeSettings,
  searchDirectory: string,
  cwd: string,
  entityName: string,
): Promise<DescribeJsonPayload | undefined> {
  const invocation = resolveToolInvocation(
    searchDirectory,
    settings.toolPath,
    settings.dotnetFramework,
  );
  const args = [...buildEfvibeArgs(settings), '--describe-json', entityName, '--no-banner'];

  try {
    const { stdout } = await execFileAsync(invocation.command, [...invocation.prefixArgs, ...args], {
      cwd,
      timeout: 10 * 60_000,
      maxBuffer: 4 * 1024 * 1024,
      windowsHide: true,
    });

    return parseJsonLine<DescribeJsonPayload>(stdout);
  } catch {
    return undefined;
  }
}

export async function runDbInfoJson(
  settings: EfvibeSettings,
  searchDirectory: string,
  cwd: string,
): Promise<DbInfoJsonPayload | undefined> {
  return runJsonStdout<DbInfoJsonPayload>(settings, searchDirectory, cwd, '--dbinfo-json');
}

export async function runCompletionsJson(
  settings: EfvibeSettings,
  searchDirectory: string,
  cwd: string,
  prefix: string,
): Promise<CompletionsJsonPayload | undefined> {
  const invocation = resolveToolInvocation(
    searchDirectory,
    settings.toolPath,
    settings.dotnetFramework,
  );
  const args = [...buildEfvibeArgs(settings), '--completions-json', prefix, '--no-banner'];

  try {
    const { stdout } = await execFileAsync(invocation.command, [...invocation.prefixArgs, ...args], {
      cwd,
      timeout: 10 * 60_000,
      maxBuffer: 4 * 1024 * 1024,
      windowsHide: true,
    });

    return parseJsonLine<CompletionsJsonPayload>(stdout);
  } catch {
    return undefined;
  }
}

function parseJsonLine<T>(stdout: string): T | undefined {
  const line = stdout
    .split(/\r?\n/u)
    .map((entry) => entry.trim())
    .find((entry) => entry.startsWith('{'));

  if (!line) {
    return undefined;
  }

  try {
    return JSON.parse(line) as T;
  } catch {
    return undefined;
  }
}
