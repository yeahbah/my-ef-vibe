import { execFile } from 'child_process';
import { promisify } from 'util';
import { resolveToolInvocation, type ToolInvocation } from './cliRunner';

const execFileAsync = promisify(execFile);

export interface PrerequisiteCheckResult {
  ok: boolean;
  dotnet: { found: boolean; version?: string; error?: string };
  efvibe: { found: boolean; version?: string; error?: string; invocation: ToolInvocation };
}

async function runVersion(command: string, args: string[]): Promise<string> {
  const { stdout } = await execFileAsync(command, args, {
    timeout: 30_000,
    maxBuffer: 1024 * 1024,
    windowsHide: true,
  });

  return stdout.trim();
}

export async function checkPrerequisites(workspaceRoot: string): Promise<PrerequisiteCheckResult> {
  const invocation = resolveToolInvocation(workspaceRoot, '');

  const result: PrerequisiteCheckResult = {
    ok: false,
    dotnet: { found: false },
    efvibe: { found: false, invocation },
  };

  try {
    result.dotnet.version = await runVersion('dotnet', ['--version']);
    result.dotnet.found = true;
  } catch (error) {
    result.dotnet.error = error instanceof Error ? error.message : String(error);
  }

  try {
    result.efvibe.version = await runVersion(invocation.command, [...invocation.prefixArgs, '--version']);
    result.efvibe.found = true;
  } catch (error) {
    result.efvibe.error = error instanceof Error ? error.message : String(error);
  }

  result.ok = result.dotnet.found && result.efvibe.found;
  return result;
}

export function formatPrerequisiteMessage(result: PrerequisiteCheckResult): string {
  const lines: string[] = [];

  lines.push(
    result.dotnet.found
      ? `✓ .NET SDK: ${result.dotnet.version}`
      : `✗ .NET SDK not found${result.dotnet.error ? ` — ${result.dotnet.error}` : ''}`,
  );

  const toolLabel = describeInvocation(result.efvibe.invocation);
  lines.push(
    result.efvibe.found
      ? `✓ efvibe (${toolLabel}): ${result.efvibe.version}`
      : `✗ efvibe not found (${toolLabel})${result.efvibe.error ? ` — ${result.efvibe.error}` : ''}`,
  );

  if (!result.ok) {
    lines.push('');
    lines.push('Install the .NET SDK and efvibe (`dotnet tool install -g efvibe` or add to dotnet-tools.json).');
  }

  return lines.join('\n');
}

function describeInvocation(invocation: ToolInvocation): string {
  if (invocation.kind === 'dotnet-tool') {
    const framework = invocation.framework ? ` -f ${invocation.framework}` : '';
    return `dotnet efvibe${framework}`;
  }

  if (invocation.kind === 'path') {
    return invocation.command;
  }

  return 'efvibe';
}
