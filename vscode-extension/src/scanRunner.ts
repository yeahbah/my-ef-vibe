import { execFile } from 'child_process';
import { promisify } from 'util';
import type { EfvibeSettings } from './config';
import { buildEfvibeArgs, resolveToolInvocation } from './cliRunner';
import type { ScanCiOutputDocument, ScanMode } from './scanTypes';

const execFileAsync = promisify(execFile);

export interface ScanRunOptions {
  mode: ScanMode;
  respectDismissals?: boolean;
  minSeverity?: string;
}

export interface ScanRunResult {
  exitCode: number;
  stdout: string;
  stderr: string;
  output?: ScanCiOutputDocument;
}

export function buildScanArgs(
  settings: EfvibeSettings,
  options: ScanRunOptions,
  searchDirectory?: string,
): string[] {
  const args = ['scan', options.mode, ...buildEfvibeArgs(settings, searchDirectory), '--json', '--no-banner'];

  if (options.respectDismissals) {
    args.push('--respect-dismissals');
  }

  if (options.minSeverity?.trim()) {
    args.push('--min-severity', options.minSeverity.trim());
  }

  return args;
}

function parseScanStdout(stdout: string): ScanCiOutputDocument | undefined {
  const line = stdout
    .split(/\r?\n/u)
    .map((entry) => entry.trim())
    .find((entry) => entry.startsWith('{'));

  if (!line) {
    return undefined;
  }

  try {
    return JSON.parse(line) as ScanCiOutputDocument;
  } catch {
    return undefined;
  }
}

export async function runScan(
  settings: EfvibeSettings,
  searchDirectory: string,
  cwd: string,
  options: ScanRunOptions,
): Promise<ScanRunResult> {
  const invocation = resolveToolInvocation(
    searchDirectory,
    settings.toolPath,
    settings.dotnetFramework,
  );
  const args = buildScanArgs(settings, options, searchDirectory);

  try {
    const { stdout, stderr } = await execFileAsync(invocation.command, [...invocation.prefixArgs, ...args], {
      cwd,
      timeout: 20 * 60_000,
      maxBuffer: 16 * 1024 * 1024,
      windowsHide: true,
    });

    return {
      exitCode: 0,
      stdout,
      stderr,
      output: parseScanStdout(stdout),
    };
  } catch (error) {
    const execError = error as NodeJS.ErrnoException & {
      stdout?: string;
      stderr?: string;
      code?: number;
    };

    const stdout = execError.stdout ?? '';
    const stderr = execError.stderr ?? '';

    return {
      exitCode: typeof execError.code === 'number' ? execError.code : 1,
      stdout,
      stderr,
      output: parseScanStdout(stdout),
    };
  }
}
