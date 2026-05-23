import * as vscode from 'vscode';
import { buildExpressionCommand, buildReplCommand, runExpressionJson } from './cliRunner';
import { getSearchDirectory, getWorkspaceFolder, readSettings } from './config';
import { exportEvaluationPayload } from './resultExport';
import { getDbContextSessionDirectory } from './sessionPaths';
import { getExpressionFromEditor, type ExpressionSelectionKind } from './expressionSelection';
import { checkPrerequisites, formatPrerequisiteMessage } from './prerequisites';
import { invalidateEfvibeDaemon } from './daemonClient';
import { validateReadOnlyExpression } from './expressionGuard';
import { EfvibeResultPanel, formatEvaluationForOutput, type PanelRunRequest } from './resultPanel';
import { generateReplTask } from './replTask';
import { registerScanService } from './scanService';
import { EfvibeStatusBar } from './statusBar';

const OUTPUT_CHANNEL_NAME = 'efvibe';

let statusBar: EfvibeStatusBar | undefined;
let outputChannel: vscode.OutputChannel | undefined;
let lastSql: string[] = [];

let extensionContext: vscode.ExtensionContext | undefined;

export async function activate(context: vscode.ExtensionContext): Promise<void> {
  extensionContext = context;
  outputChannel = vscode.window.createOutputChannel(OUTPUT_CHANNEL_NAME);
  context.subscriptions.push(outputChannel);

  statusBar = new EfvibeStatusBar(context);
  statusBar.showConfigured();

  registerScanService(context);

  context.subscriptions.push(
    vscode.commands.registerCommand('efvibe.startRepl', () => startRepl()),
    vscode.commands.registerCommand('efvibe.runExpression', () => runExpression()),
    vscode.commands.registerCommand('efvibe.runSelection', () => runEditorExpression('selection')),
    vscode.commands.registerCommand('efvibe.runLineAtCursor', () => runEditorExpression('line')),
    vscode.commands.registerCommand('efvibe.runStatementAtCursor', () => runEditorExpression('statement')),
    vscode.commands.registerCommand('efvibe.showLastSql', () => showLastSql()),
    vscode.commands.registerCommand('efvibe.generateReplTask', () => generateReplTaskCommand()),
    vscode.commands.registerCommand('efvibe.checkPrerequisites', () => checkPrerequisitesCommand()),
    vscode.commands.registerCommand('efvibe.refreshStatus', () => statusBar?.refresh()),
    vscode.commands.registerCommand('efvibe.exportResult', () => exportLastResultCommand()),
    vscode.workspace.onDidChangeConfiguration((event) => {
      if (event.affectsConfiguration('efvibe')) {
        if (event.affectsConfiguration('efvibe.project')
          || event.affectsConfiguration('efvibe.startupProject')
          || event.affectsConfiguration('efvibe.context')
          || event.affectsConfiguration('efvibe.workspaceRoot')
          || event.affectsConfiguration('efvibe.toolPath')
          || event.affectsConfiguration('efvibe.dotnetFramework')
          || event.affectsConfiguration('efvibe.dbLog')
          || event.affectsConfiguration('efvibe.connectionString')
          || event.affectsConfiguration('efvibe.provider')
          || event.affectsConfiguration('efvibe.useDaemon')) {
          invalidateEfvibeDaemon();
        }

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
  invalidateEfvibeDaemon();
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

async function requireWorkspaceAndSettings(): Promise<{
  folder: vscode.WorkspaceFolder;
  settings: ReturnType<typeof readSettings>;
  searchDirectory: string;
} | undefined> {
  const folder = getWorkspaceFolder();
  if (!folder) {
    vscode.window.showErrorMessage('Open a workspace folder before using efvibe.');
    return undefined;
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

    return undefined;
  }

  const searchDirectory = getSearchDirectory(settings, folder);
  const prereq = await checkPrerequisites(searchDirectory);
  if (!prereq.ok) {
    await checkPrerequisitesCommand();
    return undefined;
  }

  return { folder, settings, searchDirectory };
}

async function startRepl(): Promise<void> {
  const context = await requireWorkspaceAndSettings();
  if (!context) {
    return;
  }

  const commandLine = buildReplCommand(context.settings, context.searchDirectory);
  const terminal = vscode.window.createTerminal({
    name: 'efvibe',
    cwd: context.folder.uri.fsPath,
  });

  terminal.show();
  terminal.sendText(commandLine, true);
}

async function runExpression(): Promise<void> {
  const context = await requireWorkspaceAndSettings();
  if (!context) {
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

  await evaluateExpression(expression.trim(), context);
}

async function runEditorExpression(kind: ExpressionSelectionKind): Promise<void> {
  const context = await requireWorkspaceAndSettings();
  if (!context) {
    return;
  }

  const editor = vscode.window.activeTextEditor;
  if (!editor || editor.document.languageId !== 'csharp') {
    vscode.window.showErrorMessage('Open a C# file and place the cursor on a LINQ expression.');
    return;
  }

  const expression = getExpressionFromEditor(editor, kind);
  if (!expression) {
    const hint = kind === 'selection'
      ? 'Select a LINQ expression to run with efvibe.'
      : 'No expression found at the cursor.';
    vscode.window.showWarningMessage(hint);
    return;
  }

  await evaluateExpression(expression, context);
}

async function evaluateExpression(
  expression: string,
  context: {
    folder: vscode.WorkspaceFolder;
    settings: ReturnType<typeof readSettings>;
    searchDirectory: string;
  },
): Promise<void> {
  const guard = validateReadOnlyExpression(expression);
  if (!guard.ok) {
    vscode.window.showErrorMessage(guard.reason ?? 'Expression is not allowed.');
    return;
  }

  const destination = context.settings.resultDestination;

  if (destination === 'terminal') {
    invalidateEfvibeDaemon();

    const commandLine = buildExpressionCommand(
      context.settings,
      context.searchDirectory,
      expression,
    );
    const terminal = vscode.window.terminals.find((t) => t.name === 'efvibe')
      ?? vscode.window.createTerminal({ name: 'efvibe', cwd: context.folder.uri.fsPath });
    terminal.show();
    terminal.sendText(commandLine, true);
    return;
  }

  await vscode.window.withProgress(
    {
      location: vscode.ProgressLocation.Notification,
      title: 'efvibe',
      cancellable: false,
    },
    async () => {
      const result = await runExpressionJson(
        context.settings,
        context.searchDirectory,
        context.folder.uri.fsPath,
        expression,
        {
          preferDaemon: context.settings.useDaemon
            && context.settings.resultDestination !== 'terminal',
        },
      );

      lastSql = result.payload?.sql ?? [];
      void vscode.commands.executeCommand('setContext', 'efvibe.hasLastSql', lastSql.length > 0);

      if (!result.payload) {
        if (outputChannel) {
          outputChannel.clear();
          outputChannel.appendLine(`> ${expression}`);
          outputChannel.appendLine('');
          outputChannel.appendLine(result.stderr || result.stdout || 'No JSON output from efvibe.');
          outputChannel.show(true);
        }

        vscode.window.showErrorMessage('efvibe did not return JSON. Check the output channel.');
        return;
      }

      if (destination === 'panel') {
        if (!extensionContext) {
          return;
        }

        EfvibeResultPanel.show(
          extensionContext,
          result.payload,
          expression,
          (request) => runFromResultPanel(request, context),
          resolveExportDirectory(context.settings, context.folder),
        );

        if (!result.payload.success) {
          vscode.window.showErrorMessage(result.payload.error ?? 'efvibe evaluation failed.');
        }

        return;
      }

      if (outputChannel) {
        outputChannel.clear();
        outputChannel.appendLine(formatEvaluationForOutput(result.payload, expression));
        outputChannel.show(true);
      }

      if (!result.payload.success) {
        vscode.window.showErrorMessage(result.payload.error ?? 'efvibe evaluation failed.');
      }
    },
  );
}

async function runFromResultPanel(
  request: PanelRunRequest,
  context: {
    folder: vscode.WorkspaceFolder;
    settings: ReturnType<typeof readSettings>;
    searchDirectory: string;
  },
): Promise<void> {
  const expression = request.expression.trim();
  if (!expression) {
    vscode.window.showWarningMessage('Enter an expression to run.');
    return;
  }

  const guard = validateReadOnlyExpression(expression);
  if (!guard.ok) {
    vscode.window.showErrorMessage(guard.reason ?? 'Expression is not allowed.');
    return;
  }

  await vscode.window.withProgress(
    {
      location: vscode.ProgressLocation.Notification,
      title: request.withPlan ? 'efvibe :plan' : 'efvibe',
      cancellable: false,
    },
    async () => {
      const result = await runExpressionJson(
        context.settings,
        context.searchDirectory,
        context.folder.uri.fsPath,
        expression,
        { withPlan: request.withPlan, preferDaemon: context.settings.useDaemon },
      );

      lastSql = result.payload?.sql ?? [];
      void vscode.commands.executeCommand('setContext', 'efvibe.hasLastSql', lastSql.length > 0);

      if (!result.payload) {
        vscode.window.showErrorMessage('efvibe did not return JSON. Check the output channel.');
        return;
      }

      if (!extensionContext) {
        return;
      }

      EfvibeResultPanel.show(
        extensionContext,
        result.payload,
        expression,
        (next) => runFromResultPanel(next, context),
        resolveExportDirectory(context.settings, context.folder),
      );

      if (!result.payload.success) {
        vscode.window.showErrorMessage(result.payload.error ?? 'efvibe evaluation failed.');
      } else if (request.withPlan && result.payload.queryPlanNote && !result.payload.queryPlan) {
        vscode.window.showWarningMessage(result.payload.queryPlanNote);
      }
    },
  );
}

function resolveExportDirectory(
  settings: ReturnType<typeof readSettings>,
  folder: vscode.WorkspaceFolder,
): string {
  if (settings.project && settings.context) {
    return getDbContextSessionDirectory(
      settings.workspaceRoot,
      settings.project,
      settings.context,
    );
  }

  return folder.uri.fsPath;
}

async function exportLastResultCommand(): Promise<void> {
  const payload = EfvibeResultPanel.getLastPayload();
  if (!payload) {
    vscode.window.showInformationMessage('Run an expression in the result panel first.');
    return;
  }

  const format = await vscode.window.showQuickPick(
    [
      { label: 'CSV', description: 'Comma-separated values', value: 'csv' as const },
      { label: 'JSON', description: 'JSON array of row objects', value: 'json' as const },
    ],
    { title: 'efvibe :export' },
  );

  if (!format) {
    return;
  }

  const folder = getWorkspaceFolder();
  const settings = readSettings(folder);
  const exportDirectory = folder
    ? resolveExportDirectory(settings, folder)
    : settings.workspaceRoot;

  await exportEvaluationPayload(payload, format.value, exportDirectory);
}

async function showLastSql(): Promise<void> {
  if (!lastSql.length) {
    vscode.window.showInformationMessage('Run an expression first to capture SQL.');
    return;
  }

  if (outputChannel) {
    outputChannel.clear();
    outputChannel.appendLine('Last efvibe SQL:');
    outputChannel.appendLine('');

    for (const sql of lastSql) {
      outputChannel.appendLine(sql);
      outputChannel.appendLine('');
    }

    outputChannel.show(true);
  }
}

async function generateReplTaskCommand(): Promise<void> {
  const context = await requireWorkspaceAndSettings();
  if (!context) {
    return;
  }

  await generateReplTask(context.settings, context.searchDirectory, context.folder);
}
