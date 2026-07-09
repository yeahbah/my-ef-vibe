import { spawn, type ChildProcessWithoutNullStreams } from 'child_process';
import type { EfvibeSettings } from './config';
import { buildServeArgs, resolveToolInvocation, type ExpressionRunResult } from './cliRunner';
import { parseEvaluationJson } from './evaluationTypes';

const READY_TIMEOUT_MS = 10 * 60_000;
const COMMAND_TIMEOUT_MS = 10 * 60_000;
const SCAN_TIMEOUT_MS = 20 * 60_000;

interface DaemonState {
  key: string;
  process: ChildProcessWithoutNullStreams;
  ready: Promise<void>;
  stdoutBuffer: string;
  pendingLine?: {
    resolve: (line: string) => void;
    reject: (error: Error) => void;
    timer: NodeJS.Timeout;
  };
}

let daemonState: DaemonState | undefined;
/** Bumped on invalidate so queued work aborts instead of writing to a dead session. */
let sessionGeneration = 0;
/** Serializes all daemon stdin/stdout traffic (one in-flight request per process). */
let serialTail: Promise<void> = Promise.resolve();

function settingsKey(settings: EfvibeSettings, searchDirectory: string, cwd: string): string {
  return JSON.stringify({
    cwd,
    searchDirectory,
    workspaceRoot: settings.workspaceRoot,
    project: settings.project,
    startupProject: settings.startupProject,
    context: settings.context,
    connectionString: settings.connectionString,
    toolPath: settings.toolPath,
    dbLog: settings.dbLog,
    dotnetFramework: settings.dotnetFramework,
  });
}

function rejectPendingLine(error: Error): void {
  if (!daemonState?.pendingLine) {
    return;
  }

  const pending = daemonState.pendingLine;
  daemonState.pendingLine = undefined;
  clearTimeout(pending.timer);
  pending.reject(error);
}

function enqueueSerial<T>(work: () => Promise<T>): Promise<T> {
  const generation = sessionGeneration;
  const run = serialTail.then(async () => {
    if (generation !== sessionGeneration) {
      throw new Error('efvibe daemon session invalidated.');
    }

    return work();
  });
  serialTail = run.then(
    () => undefined,
    () => undefined,
  );
  return run;
}

function disposeDaemon(): void {
  if (!daemonState) {
    return;
  }

  rejectPendingLine(new Error('efvibe daemon stopped.'));
  daemonState.process.kill();
  daemonState = undefined;
}

function appendStdout(chunk: string): void {
  if (!daemonState) {
    return;
  }

  daemonState.stdoutBuffer += chunk;

  while (true) {
    const newlineIndex = daemonState.stdoutBuffer.indexOf('\n');
    if (newlineIndex < 0) {
      break;
    }

    const line = daemonState.stdoutBuffer.slice(0, newlineIndex).trim();
    daemonState.stdoutBuffer = daemonState.stdoutBuffer.slice(newlineIndex + 1);

    if (!line) {
      continue;
    }

    if (daemonState.pendingLine) {
      const pending = daemonState.pendingLine;
      daemonState.pendingLine = undefined;
      clearTimeout(pending.timer);
      pending.resolve(line);
    }
  }
}

function waitForLine(timeoutMs: number): Promise<string> {
  if (!daemonState) {
    return Promise.reject(new Error('efvibe daemon is not running.'));
  }

  if (daemonState.pendingLine) {
    return Promise.reject(new Error('efvibe daemon protocol desynchronized (duplicate waiter).'));
  }

  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => {
      if (daemonState?.pendingLine) {
        daemonState.pendingLine = undefined;
      }

      reject(new Error('efvibe daemon timed out waiting for a response.'));
    }, timeoutMs);

    daemonState!.pendingLine = { resolve, reject, timer };
    drainStdoutBuffer();
  });
}

function drainStdoutBuffer(): void {
  appendStdout('');
}

function parseServeHandshake(line: string): { type?: string; message?: string } | undefined {
  if (!line.startsWith('{')) {
    return undefined;
  }

  try {
    return JSON.parse(line) as { type?: string; message?: string };
  } catch {
    return undefined;
  }
}

async function waitForServeHandshake(timeoutMs: number): Promise<{ type?: string; message?: string }> {
  const deadline = Date.now() + timeoutMs;

  while (Date.now() < deadline) {
    const remaining = Math.max(1, deadline - Date.now());
    const line = await waitForLine(remaining);
    const payload = parseServeHandshake(line);

    if (payload) {
      return payload;
    }
  }

  throw new Error('efvibe serve timed out during workspace load.');
}

function parseServeError(line: string): string | undefined {
  try {
    const payload = JSON.parse(line) as { type?: string; message?: string };
    if (payload.type === 'error') {
      return payload.message ?? 'efvibe daemon error.';
    }
  } catch {
    // Not a serve error envelope; caller handles the line.
  }

  return undefined;
}

async function writeRequestAndWaitForLine(requestJson: string, timeoutMs: number): Promise<string> {
  if (!daemonState) {
    throw new Error('efvibe daemon failed to start.');
  }

  const linePromise = waitForLine(timeoutMs);
  daemonState.process.stdin.write(`${requestJson}\n`);
  const line = await linePromise;
  const error = parseServeError(line);
  if (error) {
    throw new Error(error);
  }

  return line;
}

async function ensureDaemonReady(
  settings: EfvibeSettings,
  searchDirectory: string,
  cwd: string,
): Promise<void> {
  const key = settingsKey(settings, searchDirectory, cwd);

  if (daemonState?.key === key) {
    await daemonState.ready;
    return;
  }

  disposeDaemon();

  const invocation = resolveToolInvocation(
    searchDirectory,
    settings.toolPath,
    settings.dotnetFramework,
  );
  const args = [...invocation.prefixArgs, ...buildServeArgs(settings, searchDirectory)];

  const child = spawn(invocation.command, args, {
    cwd,
    stdio: ['pipe', 'pipe', 'pipe'],
    windowsHide: true,
  });

  let readyResolve!: () => void;
  let readyReject!: (error: Error) => void;

  const ready = new Promise<void>((resolve, reject) => {
    readyResolve = resolve;
    readyReject = reject;
  });

  daemonState = {
    key,
    process: child,
    ready,
    stdoutBuffer: '',
  };

  child.stdout.setEncoding('utf8');
  child.stdout.on('data', (data: string) => appendStdout(data));

  child.stderr.setEncoding('utf8');
  child.stderr.on('data', () => {
    // Build progress / diagnostics only; responses are stdout JSON lines.
  });

  child.on('error', (error) => {
    readyReject(error);
    disposeDaemon();
  });

  child.on('exit', () => {
    disposeDaemon();
  });

  const readyTimer = setTimeout(() => {
    readyReject(new Error('efvibe serve timed out during workspace load.'));
    disposeDaemon();
  }, READY_TIMEOUT_MS);

  waitForServeHandshake(READY_TIMEOUT_MS)
    .then((payload) => {
      clearTimeout(readyTimer);

      if (payload.type === 'ready') {
        readyResolve();
        return;
      }

      if (payload.type === 'error') {
        readyReject(new Error(payload.message ?? 'efvibe serve failed to start.'));
        disposeDaemon();
        return;
      }

      readyReject(new Error(`Unexpected serve handshake: ${JSON.stringify(payload)}`));
      disposeDaemon();
    })
    .catch((error) => {
      clearTimeout(readyTimer);
      readyReject(error instanceof Error ? error : new Error(String(error)));
      disposeDaemon();
    });

  await ready;
}

export function invalidateEfvibeDaemon(): void {
  sessionGeneration++;
  disposeDaemon();
}

/** Sends one serve request and returns the single JSON response line (serialized with other daemon calls). */
export async function runDaemonJson(
  settings: EfvibeSettings,
  searchDirectory: string,
  cwd: string,
  request: Record<string, unknown>,
  timeoutMs: number = COMMAND_TIMEOUT_MS,
): Promise<string> {
  return enqueueSerial(async () => {
    await ensureDaemonReady(settings, searchDirectory, cwd);
    return writeRequestAndWaitForLine(JSON.stringify(request), timeoutMs);
  });
}

export async function runExpressionViaDaemon(
  settings: EfvibeSettings,
  searchDirectory: string,
  cwd: string,
  expression: string,
  withPlan = false,
): Promise<ExpressionRunResult> {
  return enqueueSerial(async () => {
    await ensureDaemonReady(settings, searchDirectory, cwd);

    const request = JSON.stringify({ type: 'eval', expression, withPlan });
    const line = await writeRequestAndWaitForLine(request, COMMAND_TIMEOUT_MS);
    const payload = parseEvaluationJson(line);

    return {
      exitCode: payload?.success ? 0 : 20,
      stdout: line,
      stderr: '',
      payload,
    };
  });
}

export async function runDbInfoViaDaemon(
  settings: EfvibeSettings,
  searchDirectory: string,
  cwd: string,
): Promise<string> {
  return runDaemonJson(settings, searchDirectory, cwd, { type: 'dbinfo' });
}

export async function runTablesViaDaemon(
  settings: EfvibeSettings,
  searchDirectory: string,
  cwd: string,
): Promise<string> {
  return runDaemonJson(settings, searchDirectory, cwd, { type: 'tables' });
}

export async function runDescribeViaDaemon(
  settings: EfvibeSettings,
  searchDirectory: string,
  cwd: string,
  entityName: string,
): Promise<string> {
  return runDaemonJson(settings, searchDirectory, cwd, { type: 'describe', entity: entityName });
}

export async function runCompletionsViaDaemon(
  settings: EfvibeSettings,
  searchDirectory: string,
  cwd: string,
  prefix: string,
): Promise<string> {
  return runDaemonJson(settings, searchDirectory, cwd, { type: 'completions', prefix });
}

export async function runScanViaDaemon(
  settings: EfvibeSettings,
  searchDirectory: string,
  cwd: string,
  options: { mode: string; respectDismissals?: boolean; minSeverity?: string },
): Promise<string> {
  const request: Record<string, unknown> = {
    type: 'scan',
    mode: options.mode,
    respectDismissals: options.respectDismissals ?? false,
  };

  if (options.minSeverity?.trim()) {
    request.minSeverity = options.minSeverity.trim();
  }

  return runDaemonJson(settings, searchDirectory, cwd, request, SCAN_TIMEOUT_MS);
}
