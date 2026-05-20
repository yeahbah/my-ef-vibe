import * as vscode from 'vscode';
import { buildExpressionCommand, buildReplCommand } from './cliRunner';
import { getSearchDirectory, getWorkspaceFolder, readSettings } from './config';
import { checkPrerequisites, formatPrerequisiteMessage } from './prerequisites';
import { EfvibeStatusBar } from './statusBar';

const OUTPUT_CHANNEL_NAME = 'efvibe';

let statusBar: EfvibeStatusBar | undefined;
let outputChannel: vscode.OutputChannel | undefined;

export async function activate(context: vscode.ExtensionContext): Promise<void> {
  outputChannel = vscode.window.createOutputChannel(OUTPUT_CHANNEL_NAME);
  context.subscriptions.push(outputChannel);

  statusBar = new EfvibeStatusBar(context);
  statusBar.showConfigured();

  context.subscriptions.push(
    vscode.commands.registerCommand('efvibe.startRepl', () => startRepl()),
    vscode.commands.registerCommand('efvibe.runExpression', () => runExpression()),
    vscode.commands.registerCommand('efvibe.checkPrerequisites', () => checkPrerequisitesCommand()),
    vscode.commands.registerCommand('efvibe.refreshStatus', () => statusBar?.refresh()),
    vscode.workspace.onDidChangeConfiguration((event) => {
      if (event.affectsConfiguration('efvibe')) {
        statusBar?.showConfigured();
      }
    }),
  );

  const folder = getWorkspaceFolder();
  if (folder) {
    void checkPrerequisitesOnActivate(folder.uri.fsPath);
  }
}

export function deactivate(): void {
  statusBar?.dispose();
  statusBar = undefined;
}

async function checkPrerequisitesOnActivate(workspaceRoot: string): Promise<void> {
  const result = await checkPrerequisites(workspaceRoot);
  if (result.ok) {
    void statusBar?.refresh();
    return;
  }

  const message = formatPrerequisiteMessage(result);
  outputChannel?.appendLine(message);

  const choice = await vscode.window.showWarningMessage(
    'efvibe prerequisites are missing or not on PATH.',
    'Check prerequisites',
    'Dismiss',
  );

  if (choice === 'Check prerequisites') {
    await checkPrerequisitesCommand();
  }
}

async function checkPrerequisitesCommand(): Promise<void> {
  const folder = getWorkspaceFolder();
  const workspaceRoot = folder?.uri.fsPath ?? process.cwd();
  const result = await checkPrerequisites(workspaceRoot);
  const message = formatPrerequisiteMessage(result);

  if (outputChannel) {
    outputChannel.clear();
    outputChannel.appendLine(message);
    outputChannel.show(true);
  }

  if (result.ok) {
    vscode.window.showInformationMessage(`efvibe ready (${result.efvibe.version}).`);
    await statusBar?.refresh();
  } else {
    vscode.window.showWarningMessage('efvibe prerequisites check failed. See output for details.');
  }
}

async function startRepl(): Promise<void> {
  const folder = getWorkspaceFolder();
  if (!folder) {
    vscode.window.showErrorMessage('Open a workspace folder before starting efvibe.');
    return;
  }

  const settings = readSettings(folder);
  if (!settings.project) {
    const configure = await vscode.window.showWarningMessage(
      'Set efvibe.project in settings (path to your EF Core .csproj).',
      'Open Settings',
    );

    if (configure === 'Open Settings') {
      await vscode.commands.executeCommand(
        'workbench.action.openSettings',
        'efvibe.project',
      );
    }

    return;
  }

  const searchDirectory = getSearchDirectory(settings, folder);
  const prereq = await checkPrerequisites(searchDirectory);
  if (!prereq.ok) {
    await checkPrerequisitesCommand();
    return;
  }

  const commandLine = buildReplCommand(settings, searchDirectory);
  const terminal = vscode.window.createTerminal({
    name: 'efvibe',
    cwd: folder.uri.fsPath,
  });

  terminal.show();
  terminal.sendText(commandLine, true);
}

async function runExpression(): Promise<void> {
  const folder = getWorkspaceFolder();
  if (!folder) {
    vscode.window.showErrorMessage('Open a workspace folder before running an expression.');
    return;
  }

  const settings = readSettings(folder);
  if (!settings.project) {
    vscode.window.showErrorMessage('Set efvibe.project in settings before running expressions.');
    return;
  }

  const editor = vscode.window.activeTextEditor;
  const selection = editor?.document.getText(editor.selection).trim();
  const expression = selection
    ? selection
    : await vscode.window.showInputBox({
        title: 'efvibe expression',
        placeHolder: 'db.Products.Count()',
        prompt: 'LINQ expression to evaluate with efvibe -e',
      });

  if (!expression?.trim()) {
    return;
  }

  const searchDirectory = getSearchDirectory(settings, folder);
  const commandLine = buildExpressionCommand(settings, searchDirectory, expression.trim());

  const useOutput = await vscode.window.showQuickPick(
    [
      { label: 'Integrated terminal', value: 'terminal' as const },
      { label: 'Output channel', value: 'output' as const },
    ],
    { title: 'Run efvibe expression' },
  );

  if (!useOutput) {
    return;
  }

  if (useOutput.value === 'terminal') {
    const terminal = vscode.window.terminals.find((t) => t.name === 'efvibe')
      ?? vscode.window.createTerminal({ name: 'efvibe', cwd: folder.uri.fsPath });
    terminal.show();
    terminal.sendText(commandLine, true);
    return;
  }

  if (outputChannel) {
    outputChannel.clear();
    outputChannel.appendLine(`> ${commandLine}`);
    outputChannel.appendLine('');
    outputChannel.appendLine('(Run in terminal for interactive output; JSON output arrives in Phase 1.)');
    outputChannel.show(true);
  }

  const terminal = vscode.window.createTerminal({ name: 'efvibe', cwd: folder.uri.fsPath });
  terminal.show();
  terminal.sendText(commandLine, true);
}
