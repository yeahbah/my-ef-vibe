import * as vscode from 'vscode';
import { getSearchDirectory, getWorkspaceFolder, readSettings } from './config';
import { runDbInfoJson, runExpressionJson, runTablesJson } from './cliRunner';
import type { DbInfoJsonPayload, TablesJsonPayload } from './cliRunner';
import type { EvaluationJsonPayload } from './evaluationTypes';
import { validateReadOnlyExpression } from './expressionGuard';

const NOTEBOOK_TYPE = 'efvibe-notebook';
const CODE_LANGUAGE = 'csharp';
const MARKDOWN_LANGUAGE = 'markdown';

interface SerializedNotebook {
  cells?: SerializedCell[];
}

interface SerializedCell {
  kind?: 'code' | 'markdown';
  languageId?: string;
  value?: string;
  metadata?: Record<string, unknown>;
  outputs?: SerializedOutput[];
}

interface SerializedOutput {
  items?: SerializedOutputItem[];
}

interface SerializedOutputItem {
  mime?: string;
  value?: string;
}

export function registerEfvibeNotebook(context: vscode.ExtensionContext): void {
  context.subscriptions.push(
    vscode.workspace.registerNotebookSerializer(NOTEBOOK_TYPE, new EfvibeNotebookSerializer(), {
      transientOutputs: false,
      transientCellMetadata: {},
    }),
    createController('efvibe-notebook', 'efvibe', false),
    createController('efvibe-notebook-plan', 'efvibe :plan', true),
    vscode.commands.registerCommand('efvibe.newNotebook', () => createNewNotebook()),
  );
}

class EfvibeNotebookSerializer implements vscode.NotebookSerializer {
  async deserializeNotebook(content: Uint8Array): Promise<vscode.NotebookData> {
    const text = new TextDecoder().decode(content).trim();

    if (!text) {
      return new vscode.NotebookData([createCodeCell('db.Products.Take(10)')]);
    }

    try {
      const parsed = JSON.parse(text) as SerializedNotebook;
      const cells = (parsed.cells ?? []).map((cell) => {
        const kind = cell.kind === 'markdown'
          ? vscode.NotebookCellKind.Markup
          : vscode.NotebookCellKind.Code;

        const data = new vscode.NotebookCellData(
          kind,
          cell.value ?? '',
          kind === vscode.NotebookCellKind.Markup
            ? MARKDOWN_LANGUAGE
            : cell.languageId || CODE_LANGUAGE,
        );

        data.outputs = (cell.outputs ?? []).map((output) => new vscode.NotebookCellOutput(
          (output.items ?? []).map((item) => vscode.NotebookCellOutputItem.text(
            item.value ?? '',
            item.mime || 'text/plain',
          )),
        ));

        return data;
      });

      return new vscode.NotebookData(cells.length > 0 ? cells : [createCodeCell('db.Products.Take(10)')]);
    } catch {
      return new vscode.NotebookData([createCodeCell(text)]);
    }
  }

  async serializeNotebook(data: vscode.NotebookData): Promise<Uint8Array> {
    const payload: SerializedNotebook = {
      cells: data.cells.map((cell) => ({
        kind: cell.kind === vscode.NotebookCellKind.Markup ? 'markdown' : 'code',
        languageId: cell.languageId,
        value: cell.value,
        metadata: cell.metadata,
        outputs: (cell.outputs ?? []).map((output) => ({
          items: output.items.map((item) => ({
            mime: item.mime,
            value: new TextDecoder().decode(item.data),
          })),
        })),
      })),
    };

    return new TextEncoder().encode(`${JSON.stringify(payload, null, 2)}\n`);
  }
}

function createController(id: string, label: string, withPlan: boolean): vscode.NotebookController {
  const controller = vscode.notebooks.createNotebookController(id, NOTEBOOK_TYPE, label);
  controller.supportedLanguages = [CODE_LANGUAGE, 'efvibe'];
  controller.supportsExecutionOrder = true;
  controller.executeHandler = async (cells, notebook, controllerRef) => {
    for (const cell of cells) {
      await executeCell(cell, notebook, controllerRef, withPlan);
    }
  };

  return controller;
}

async function executeCell(
  cell: vscode.NotebookCell,
  notebook: vscode.NotebookDocument,
  controller: vscode.NotebookController,
  withPlan: boolean,
): Promise<void> {
  if (cell.kind !== vscode.NotebookCellKind.Code) {
    return;
  }

  const execution = controller.createNotebookCellExecution(cell);
  execution.executionOrder = Date.now();
  execution.start(Date.now());

  try {
    const source = cell.document.getText().trim();

    if (!source) {
      execution.replaceOutput([new vscode.NotebookCellOutput([
        vscode.NotebookCellOutputItem.stderr('Cell is empty.'),
      ])]);
      execution.end(false, Date.now());
      return;
    }

    const guard = validateReadOnlyExpression(source);

    if (!guard.ok) {
      execution.replaceOutput([new vscode.NotebookCellOutput([
        vscode.NotebookCellOutputItem.stderr(guard.reason ?? 'Expression is not allowed.'),
      ])]);
      execution.end(false, Date.now());
      return;
    }

    const context = resolveNotebookContext(notebook.uri);

    if (!context) {
      execution.replaceOutput([new vscode.NotebookCellOutput([
        vscode.NotebookCellOutputItem.stderr('Open a workspace folder before running an efvibe notebook.'),
      ])]);
      execution.end(false, Date.now());
      return;
    }

    if (source.startsWith(':')) {
      await executeCommandCell(source, context, execution);
      return;
    }

    const result = await runExpressionJson(
      context.settings,
      context.searchDirectory,
      context.folder.uri.fsPath,
      source,
      { withPlan, preferDaemon: context.settings.useDaemon },
    );

    if (!result.payload) {
      execution.replaceOutput([new vscode.NotebookCellOutput([
        vscode.NotebookCellOutputItem.stderr(result.stderr || result.stdout || 'No JSON output from efvibe.'),
      ])]);
      execution.end(false, Date.now());
      return;
    }

    execution.replaceOutput(buildEvaluationOutputs(result.payload));
    execution.end(result.payload.success, Date.now());
  } catch (error) {
    execution.replaceOutput([new vscode.NotebookCellOutput([
      vscode.NotebookCellOutputItem.stderr(error instanceof Error ? error.message : String(error)),
    ])]);
    execution.end(false, Date.now());
  }
}

async function executeCommandCell(
  source: string,
  context: NotebookContext,
  execution: vscode.NotebookCellExecution,
): Promise<void> {
  const command = source.trim().toLowerCase();

  if (command === ':dbinfo') {
    const payload = await runDbInfoJson(context.settings, context.searchDirectory, context.folder.uri.fsPath);
    execution.replaceOutput([new vscode.NotebookCellOutput([
      vscode.NotebookCellOutputItem.text(formatDbInfo(payload), 'text/markdown'),
    ])]);
    execution.end(Boolean(payload), Date.now());
    return;
  }

  if (command === ':tables') {
    const payload = await runTablesJson(context.settings, context.searchDirectory, context.folder.uri.fsPath);
    execution.replaceOutput([new vscode.NotebookCellOutput([
      vscode.NotebookCellOutputItem.text(formatTables(payload), 'text/markdown'),
    ])]);
    execution.end(Boolean(payload), Date.now());
    return;
  }

  execution.replaceOutput([new vscode.NotebookCellOutput([
    vscode.NotebookCellOutputItem.stderr('Supported command cells: :dbinfo, :tables'),
  ])]);
  execution.end(false, Date.now());
}

interface NotebookContext {
  folder: vscode.WorkspaceFolder;
  settings: ReturnType<typeof readSettings>;
  searchDirectory: string;
}

function resolveNotebookContext(uri: vscode.Uri): NotebookContext | undefined {
  const folder = vscode.workspace.getWorkspaceFolder(uri) ?? getWorkspaceFolder();

  if (!folder) {
    return undefined;
  }

  const settings = readSettings(folder);
  return {
    folder,
    settings,
    searchDirectory: getSearchDirectory(settings, folder),
  };
}

async function createNewNotebook(): Promise<void> {
  const data = new vscode.NotebookData([
    new vscode.NotebookCellData(
      vscode.NotebookCellKind.Markup,
      '# efvibe notebook\nRun LINQ cells against the configured DbContext.',
      MARKDOWN_LANGUAGE,
    ),
    createCodeCell('db.Products.Take(10)'),
    createCodeCell(':dbinfo', 'efvibe'),
  ]);

  const document = await vscode.workspace.openNotebookDocument(NOTEBOOK_TYPE, data);
  await vscode.window.showNotebookDocument(document);
}

function createCodeCell(value: string, languageId = CODE_LANGUAGE): vscode.NotebookCellData {
  return new vscode.NotebookCellData(vscode.NotebookCellKind.Code, value, languageId);
}

function buildEvaluationOutputs(payload: EvaluationJsonPayload): vscode.NotebookCellOutput[] {
  const items: vscode.NotebookCellOutputItem[] = [
    vscode.NotebookCellOutputItem.text(renderEvaluationHtml(payload), 'text/html'),
    vscode.NotebookCellOutputItem.text(renderEvaluationMarkdown(payload), 'text/markdown'),
  ];

  if (!payload.success) {
    items.push(vscode.NotebookCellOutputItem.stderr(payload.error ?? 'efvibe evaluation failed.'));
  }

  return [new vscode.NotebookCellOutput(items)];
}

function renderEvaluationHtml(payload: EvaluationJsonPayload): string {
  const rows = payload.rows ?? [];
  const table = rows.length > 0 ? renderRowsTable(rows) : `<p>${escapeHtml(payload.value ?? '(no rows)')}</p>`;
  const warnings = payload.warnings.length > 0
    ? `<h4>Warnings</h4><ul>${payload.warnings.map((warning) => `<li>${escapeHtml(warning)}</li>`).join('')}</ul>`
    : '';
  const sql = payload.sql.length > 0
    ? `<h4>SQL</h4>${payload.sql.map((entry) => `<pre>${escapeHtml(entry)}</pre>`).join('')}`
    : '';
  const plan = payload.queryPlan
    ? `<h4>Query Plan</h4><pre>${escapeHtml(payload.queryPlan)}</pre>`
    : payload.queryPlanNote
      ? `<h4>Query Plan</h4><pre>${escapeHtml(payload.queryPlanNote)}</pre>`
      : '';

  return `
    <div>
      <p><strong>${payload.success ? 'Success' : 'Failed'}</strong>
        · ${payload.metrics.totalMs} ms
        ${payload.metrics.databaseMs !== undefined ? ` · db ${payload.metrics.databaseMs} ms` : ''}
        ${payload.metrics.rowCount !== undefined ? ` · ${payload.metrics.rowCount} row(s)` : ''}
      </p>
      ${payload.error ? `<pre>${escapeHtml(payload.error)}</pre>` : ''}
      ${table}
      ${warnings}
      ${sql}
      ${plan}
    </div>
  `;
}

function renderRowsTable(rows: Array<Record<string, string>>): string {
  const columns = Object.keys(rows[0] ?? {});

  if (columns.length === 0) {
    return '<p>(empty)</p>';
  }

  return `
    <table>
      <thead><tr>${columns.map((column) => `<th>${escapeHtml(column)}</th>`).join('')}</tr></thead>
      <tbody>
        ${rows.map((row) => `<tr>${columns.map((column) => `<td>${escapeHtml(row[column] ?? '')}</td>`).join('')}</tr>`).join('')}
      </tbody>
    </table>
  `;
}

function renderEvaluationMarkdown(payload: EvaluationJsonPayload): string {
  const lines = [
    payload.success ? '**Success**' : `**Failed:** ${payload.error ?? 'efvibe evaluation failed.'}`,
    `Total: ${payload.metrics.totalMs} ms`,
  ];

  if (payload.metrics.databaseMs !== undefined) {
    lines.push(`Database: ${payload.metrics.databaseMs} ms`);
  }

  if (payload.metrics.rowCount !== undefined) {
    lines.push(`Rows: ${payload.metrics.rowCount}`);
  }

  return lines.join('\n\n');
}

function formatDbInfo(payload: DbInfoJsonPayload | undefined): string {
  if (!payload) {
    return 'Could not load `:dbinfo`.';
  }

  const rows = payload.entries
    .map((entry) => `| ${escapeMarkdown(entry.key)} | ${escapeMarkdown(entry.value ?? '')} |`)
    .join('\n');

  return `### DbInfo: ${escapeMarkdown(payload.dbContext)}\n\n| Key | Value |\n|---|---|\n${rows}`;
}

function formatTables(payload: TablesJsonPayload | undefined): string {
  if (!payload) {
    return 'Could not load `:tables`.';
  }

  const rows = payload.tables
    .map((entry) => `| ${escapeMarkdown(entry.dbSet)} | ${escapeMarkdown(entry.entityType)} |`)
    .join('\n');

  return `### Tables: ${escapeMarkdown(payload.dbContext)}\n\n| DbSet | Entity |\n|---|---|\n${rows}`;
}

function escapeHtml(value: string | undefined | null): string {
  return (value ?? '')
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;');
}

function escapeMarkdown(value: string | undefined | null): string {
  return (value ?? '').replace(/\|/g, '\\|').replace(/\r?\n/g, ' ');
}
