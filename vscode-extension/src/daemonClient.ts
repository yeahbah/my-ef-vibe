import { spawn, type ChildProcessWithoutNullStreams } from 'child_process';
import type { EfvibeSettings } from './config';
import { buildServeArgs, resolveToolInvocation, type ExpressionRunResult } from './cliRunner';
import { parseEvaluationJson } from './evaluationTypes';

const READY_TIMEOUT_MS = 10 * 60_000;
const EVAL_TIMEOUT_MS = 10 * 60_000;

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

function settingsKey(settings: EfvibeSettings, searchDirectory: string, cwd: string): string {
  return JSON.stringify({
    cwd,
    searchDirectory,
    workspaceRoot: settings.workspaceRoot,
    project: settings.project,
    startupProject: settings.startupProject,
    context: settings.context,
    connectionString: settings.connectionString,
    provider: settings.provider,
    toolPath: settings.toolPath,
    dbLog: settings.dbLog,
    dotnetFramework: settings.dotnetFramework,
  });
}

function disposeDaemon(): void {
  if (!daemonState) {
    return;
  }

  if (daemonState.pendingLine) {
    clearTimeout(daemonState.pendingLine.timer);
    daemonState.pendingLine.reject(new Error('efvibe daemon stopped.'));
    daemonState.pendingLine = undefined;
  }

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
    return Promise.reject(new Error('efvibe daemon request already in flight.'));
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
  const args = [...invocation.prefixArgs, ...buildServeArgs(settings)];

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

  waitForLine(READY_TIMEOUT_MS)
    .then((line) => {
      clearTimeout(readyTimer);

      try {
        const payload = JSON.parse(line) as { type?: string; message?: string };
        if (payload.type === 'ready') {
          readyResolve();
          return;
        }

        if (payload.type === 'error') {
          readyReject(new Error(payload.message ?? 'efvibe serve failed to start.'));
          disposeDaemon();
          return;
        }

        readyReject(new Error(`Unexpected serve handshake: ${line}`));
        disposeDaemon();
      } catch (error) {
        readyReject(error instanceof Error ? error : new Error(String(error)));
        disposeDaemon();
      }
    })
    .catch((error) => {
      clearTimeout(readyTimer);
      readyReject(error instanceof Error ? error : new Error(String(error)));
      disposeDaemon();
    });

  await ready;
}

export function invalidateEfvibeDaemon(): void {
  disposeDaemon();
}

export async function runExpressionViaDaemon(
  settings: EfvibeSettings,
  searchDirectory: string,
  cwd: string,
  expression: string,
  withPlan = false,
): Promise<ExpressionRunResult> {
  await ensureDaemonReady(settings, searchDirectory, cwd);

  if (!daemonState) {
    throw new Error('efvibe daemon failed to start.');
  }

  const request = JSON.stringify({ type: 'eval', expression, withPlan }) + '\n';
  daemonState.process.stdin.write(request);

  const line = await waitForLine(EVAL_TIMEOUT_MS);
  const payload = parseEvaluationJson(line);

  return {
    exitCode: payload?.success ? 0 : 20,
    stdout: line,
    stderr: '',
    payload,
  };
}
