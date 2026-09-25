/**
 * Quote argv for `Terminal.sendText` / shell tasks.
 * The integrated terminal is a real shell: unquoted `;`, `|`, `$()`, and `()`
 * are syntax, not data. Single quotes are literal in bash, zsh, and PowerShell.
 */

export type ShellFamily = 'powershell' | 'cmd' | 'posix';

export function detectShellFamily(
  shellPath: string,
  platform: NodeJS.Platform = process.platform,
): ShellFamily {
  const base = shellPath.replace(/\\/g, '/').split('/').pop()?.toLowerCase() ?? '';

  if (base === 'powershell' || base === 'powershell.exe' || base === 'pwsh' || base === 'pwsh.exe') {
    return 'powershell';
  }

  if (base === 'cmd.exe' || base === 'command.com') {
    return 'cmd';
  }

  if (platform === 'win32' && base.length === 0) {
    // VS Code's default Windows terminal profile is PowerShell.
    return 'powershell';
  }

  return 'posix';
}

export function quoteForIntegratedTerminal(value: string, shellPath: string): string {
  switch (detectShellFamily(shellPath)) {
    case 'powershell':
      return `'${value.replace(/'/g, "''")}'`;
    case 'cmd':
      return `"${value.replace(/"/g, '""')}"`;
    default:
      return `'${value.replace(/'/g, `'\\''`)}'`;
  }
}

export function joinShellCommand(tokens: readonly string[], shellPath: string): string {
  return tokens.map((token) => quoteForIntegratedTerminal(token, shellPath)).join(' ');
}
