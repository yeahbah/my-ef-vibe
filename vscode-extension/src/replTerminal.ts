import * as vscode from 'vscode';
import { buildReplCommand } from './cliRunner';
import type { EfvibeSettings } from './config';

/** Terminals we have already launched `efvibe` in (or user started via Start REPL). */
const replBootstrapped = new WeakSet<vscode.Terminal>();

let managedTerminal: vscode.Terminal | undefined;

const REPL_BOOTSTRAP_MS = 8000;

export function registerReplTerminal(context: vscode.ExtensionContext): void {
  context.subscriptions.push(
    vscode.window.onDidCloseTerminal((terminal) => {
      replBootstrapped.delete(terminal);

      if (terminal === managedTerminal) {
        managedTerminal = undefined;
      }
    }),
  );
}

/**
 * Collapse editor selection to one REPL submission. The REPL runs on `;` (see QueryRepl).
 * Repository-style snippets are normalized inside the CLI once the line is read.
 */
export function formatSnippetForReplSubmit(snippet: string): string {
  const collapsed = snippet
    .replace(/\r?\n/g, ' ')
    .replace(/\s+/g, ' ')
    .trim();

  if (!collapsed) {
    return '';
  }

  return collapsed.endsWith(';') ? collapsed : `${collapsed};`;
}

export async function ensureEfvibeReplTerminal(
  settings: EfvibeSettings,
  workspaceFolder: vscode.WorkspaceFolder,
  searchDirectory: string,
): Promise<{ terminal: vscode.Terminal; justBootstrapped: boolean }> {
  let terminal = vscode.window.terminals.find((entry) => entry.name === 'efvibe');

  if (!terminal) {
    terminal = vscode.window.createTerminal({
      name: 'efvibe',
      cwd: workspaceFolder.uri.fsPath,
    });
    managedTerminal = terminal;
  }

  terminal.show();

  const justBootstrapped = !replBootstrapped.has(terminal);

  if (justBootstrapped) {
    const commandLine = buildReplCommand(settings, searchDirectory);
    terminal.sendText(commandLine, true);
    replBootstrapped.add(terminal);

    await vscode.window.withProgress(
      {
        location: vscode.ProgressLocation.Notification,
        title: 'efvibe',
        cancellable: false,
      },
      async () => {
        await sleep(REPL_BOOTSTRAP_MS);
      },
    );
  }

  return { terminal, justBootstrapped };
}

export async function startEfvibeRepl(
  settings: EfvibeSettings,
  workspaceFolder: vscode.WorkspaceFolder,
  searchDirectory: string,
): Promise<vscode.Terminal> {
  const { terminal } = await ensureEfvibeReplTerminal(settings, workspaceFolder, searchDirectory);
  return terminal;
}

export async function sendSnippetToRepl(
  settings: EfvibeSettings,
  workspaceFolder: vscode.WorkspaceFolder,
  searchDirectory: string,
  snippet: string,
): Promise<void> {
  const line = formatSnippetForReplSubmit(snippet);

  if (!line) {
    vscode.window.showWarningMessage('Nothing to send to the REPL.');
    return;
  }

  const { terminal } = await ensureEfvibeReplTerminal(settings, workspaceFolder, searchDirectory);

  // One line only — VS Code executes each line separately in the shell otherwise.
  terminal.sendText(line, true);
}

export function markReplBootstrapped(terminal: vscode.Terminal): void {
  replBootstrapped.add(terminal);
}

function sleep(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}
