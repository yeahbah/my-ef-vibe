import * as vscode from 'vscode';
import type { EvaluationJsonPayload } from './evaluationTypes';
import { canExportPayload, exportEvaluationPayload } from './resultExport';

const VIEW_TYPE = 'efvibe.result';

export interface PanelRunRequest {
  expression: string;
  withPlan: boolean;
}

export type PanelRunHandler = (request: PanelRunRequest) => Promise<void>;

export class EfvibeResultPanel {
  private static current: EfvibeResultPanel | undefined;

  private readonly panel: vscode.WebviewPanel;

  private onRun: PanelRunHandler;

  private lastPayload: EvaluationJsonPayload | undefined;

  private exportDirectory: string | undefined;

  private constructor(
    panel: vscode.WebviewPanel,
    onRun: PanelRunHandler,
    exportDirectory?: string,
  ) {
    this.panel = panel;
    this.onRun = onRun;
    this.exportDirectory = exportDirectory;

    panel.webview.onDidReceiveMessage(async (message: { type?: string; expression?: string; text?: string }) => {
      if (message.type === 'run' || message.type === 'plan') {
        const expression = typeof message.expression === 'string' ? message.expression : '';
        await this.onRun({
          expression,
          withPlan: message.type === 'plan',
        });
        return;
      }

      if (message.type === 'exportCsv' || message.type === 'exportJson') {
        if (!this.lastPayload) {
          return;
        }

        await exportEvaluationPayload(
          this.lastPayload,
          message.type === 'exportCsv' ? 'csv' : 'json',
          this.exportDirectory,
        );
        return;
      }

      if (message.type === 'copy' && typeof message.text === 'string') {
        await vscode.env.clipboard.writeText(message.text);
        void vscode.window.setStatusBarMessage('efvibe: Copied to clipboard.', 2000);
      }
    });

    panel.onDidDispose(() => {
      if (EfvibeResultPanel.current === this) {
        EfvibeResultPanel.current = undefined;
      }
    });
  }

  static show(
    context: vscode.ExtensionContext,
    payload: EvaluationJsonPayload,
    expression: string,
    onRun: PanelRunHandler,
    exportDirectory?: string,
  ): void {
    const showIn = resolveSplitViewColumn();
    const { html } = buildHtml(payload, expression);

    if (EfvibeResultPanel.current) {
      EfvibeResultPanel.current.onRun = onRun;
      EfvibeResultPanel.current.lastPayload = payload;
      EfvibeResultPanel.current.exportDirectory = exportDirectory;

      if (typeof showIn === 'number') {
        EfvibeResultPanel.current.panel.reveal(showIn);
      } else {
        EfvibeResultPanel.current.panel.reveal(showIn.viewColumn, showIn.preserveFocus);
      }

      EfvibeResultPanel.current.panel.webview.html = html;
      return;
    }

    const panel = vscode.window.createWebviewPanel(
      VIEW_TYPE,
      'My EF Vibe result',
      showIn,
      {
        enableScripts: true,
        retainContextWhenHidden: true,
        localResourceRoots: [context.extensionUri],
      },
    );

    panel.webview.html = html;
    const instance = new EfvibeResultPanel(panel, onRun, exportDirectory);
    instance.lastPayload = payload;
    EfvibeResultPanel.current = instance;
  }

  static isOpen(): boolean {
    return EfvibeResultPanel.current !== undefined;
  }

  static getLastPayload(): EvaluationJsonPayload | undefined {
    return EfvibeResultPanel.current?.lastPayload;
  }
}

function resolveSplitViewColumn(): vscode.ViewColumn | {
  viewColumn: vscode.ViewColumn;
  preserveFocus?: boolean;
} {
  if (vscode.window.activeTextEditor) {
    return { viewColumn: vscode.ViewColumn.Beside, preserveFocus: true };
  }

  return vscode.ViewColumn.Active;
}

function buildHtml(
  payload: EvaluationJsonPayload,
  expression: string,
): { html: string } {
  const nonce = getNonce();
  const exportEnabled = canExportPayload(payload);
  const resultSection = payload.success
    ? formatResultBody(payload)
    : copyablePreBlock(payload.error ?? 'Evaluation failed.', 'error');

  const sqlBlocks = payload.sql.length > 0
    ? payload.sql.map((sql, index) => {
        const title = payload.sql.length > 1 ? `SQL ${index + 1}` : 'SQL';
        return copyableSection(title, sql, 'h3');
      }).join('')
    : '<p class="muted">No SQL captured for this run.</p>';

  const planSection = buildPlanSection(payload);

  const warnings = payload.warnings.length > 0
    ? `<section><h3>Warnings</h3><ul>${payload.warnings.map((w) => `<li>${escapeHtml(w)}</li>`).join('')}</ul></section>`
    : '';

  const metrics = [
    `${payload.metrics.totalMs} ms`,
    payload.metrics.databaseMs !== undefined ? `db ${payload.metrics.databaseMs} ms` : undefined,
    payload.metrics.rowCount !== undefined ? `${payload.metrics.rowCount} row(s)` : undefined,
    payload.metrics.sqlCommandCount > 0 ? `${payload.metrics.sqlCommandCount} command(s)` : undefined,
    payload.metrics.resultKind ? payload.metrics.resultKind : undefined,
  ].filter((entry): entry is string => Boolean(entry));

  const html = `<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8" />
  <meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; script-src 'nonce-${nonce}';" />
  <style>
    body { font-family: var(--vscode-font-family); color: var(--vscode-foreground); background: var(--vscode-editor-background); padding: 12px 16px; margin: 0; }
    h2, h3 { font-weight: 600; margin: 0 0 8px; }
    pre { background: var(--vscode-textCodeBlock-background); overflow: auto; white-space: pre-wrap; border-radius: 4px; margin: 0; }
    .copyable-box { position: relative; margin: 0; }
    .copyable-box pre.copyable-content {
      padding: 32px 12px 10px 10px;
      border: 1px solid var(--vscode-panel-border);
      box-sizing: border-box;
      font-size: 0.85rem;
    }
    .copyable-box.copyable-box-input .expr-input {
      padding-top: 32px;
    }
    .copy-btn {
      position: absolute;
      top: 6px;
      right: 6px;
      z-index: 1;
      font-size: 1rem;
      line-height: 1;
      padding: 4px 7px;
      border-radius: 4px;
      background: var(--vscode-editor-background);
      color: var(--vscode-foreground);
      border: 1px solid var(--vscode-panel-border);
      box-shadow: 0 1px 4px rgba(0, 0, 0, 0.15);
      cursor: pointer;
    }
    .copy-btn:hover { background: var(--vscode-list-hoverBackground); }
    .copy-btn.copied {
      color: var(--vscode-testing-iconPassed, var(--vscode-gitDecoration-addedResourceForeground));
      border-color: var(--vscode-testing-iconPassed, var(--vscode-gitDecoration-addedResourceForeground));
    }
    .muted { color: var(--vscode-descriptionForeground); }
    .error { color: var(--vscode-errorForeground); }
    pre.error.copyable-content { color: var(--vscode-errorForeground); }
    .expr { margin-bottom: 16px; }
    .expr .expr-input {
      width: 100%;
      min-height: 7rem;
      box-sizing: border-box;
      font-family: var(--vscode-editor-font-family);
      font-size: var(--vscode-editor-font-size);
      color: var(--vscode-editor-foreground);
      background: var(--vscode-input-background);
      border: 1px solid var(--vscode-input-border, var(--vscode-panel-border));
      border-radius: 4px;
      padding: 8px 10px;
      resize: vertical;
      line-height: 1.45;
    }
    .toolbar { display: flex; flex-wrap: wrap; gap: 8px; margin-top: 8px; align-items: center; }
    button {
      font-family: var(--vscode-font-family);
      font-size: 0.9rem;
      padding: 6px 14px;
      border-radius: 4px;
      border: 1px solid var(--vscode-button-border, transparent);
      cursor: pointer;
    }
    button.primary {
      background: var(--vscode-button-background);
      color: var(--vscode-button-foreground);
    }
    button.secondary {
      background: var(--vscode-button-secondaryBackground);
      color: var(--vscode-button-secondaryForeground);
    }
    button:disabled { opacity: 0.55; cursor: default; }
    .hint { color: var(--vscode-descriptionForeground); font-size: 0.85rem; margin: 6px 0 0; }
    .metrics { color: var(--vscode-descriptionForeground); margin-bottom: 16px; }
    table { border-collapse: collapse; width: 100%; margin-top: 8px; }
    th, td { border: 1px solid var(--vscode-panel-border); padding: 4px 8px; text-align: left; }
    th { background: var(--vscode-editor-inactiveSelectionBackground); }
    section { margin-bottom: 20px; }
  </style>
</head>
<body>
  <section class="expr">
    <h2>Expression</h2>
    <div class="copyable-box copyable-box-input">
      <button type="button" class="copy-btn" data-copy-from="expression" title="Copy to clipboard" aria-label="Copy to clipboard">📋</button>
      <textarea id="expression" class="expr-input" spellcheck="false">${escapeHtml(expression)}</textarea>
    </div>
    <div class="toolbar">
      <button class="primary" id="run">Run</button>
      <button class="secondary" id="plan">Run Plan</button>
      <button class="secondary" id="exportCsv" ${exportEnabled ? '' : 'disabled'}>Export CSV</button>
      <button class="secondary" id="exportJson" ${exportEnabled ? '' : 'disabled'}>Export JSON</button>
    </div>
    <p class="hint">Edit parameter values and re-run. Export last result like REPL <code>:export csv|json</code>. Read-only: mutations are blocked.</p>
  </section>
  <p class="metrics">${escapeHtml(metrics.join(' · '))}</p>
  <section>
    <h2>Result</h2>
    ${resultSection}
  </section>
  <section>
    <h2>SQL</h2>
    ${sqlBlocks}
  </section>
  ${planSection}
  ${warnings}
  <script nonce="${nonce}">
    const vscode = acquireVsCodeApi();
    const expressionEl = document.getElementById('expression');
    const runBtn = document.getElementById('run');
    const planBtn = document.getElementById('plan');
    const exportCsvBtn = document.getElementById('exportCsv');
    const exportJsonBtn = document.getElementById('exportJson');

    function post(type) {
      runBtn.disabled = true;
      planBtn.disabled = true;
      if (exportCsvBtn) exportCsvBtn.disabled = true;
      if (exportJsonBtn) exportJsonBtn.disabled = true;
      vscode.postMessage({ type, expression: expressionEl.value });
    }

    runBtn.addEventListener('click', () => post('run'));
    planBtn.addEventListener('click', () => post('plan'));
    if (exportCsvBtn) {
      exportCsvBtn.addEventListener('click', () => vscode.postMessage({ type: 'exportCsv' }));
    }

    if (exportJsonBtn) {
      exportJsonBtn.addEventListener('click', () => vscode.postMessage({ type: 'exportJson' }));
    }
    expressionEl.addEventListener('keydown', (event) => {
      if ((event.ctrlKey || event.metaKey) && event.key === 'Enter') {
        event.preventDefault();
        post('run');
      }
    });
    document.querySelectorAll('.copy-btn').forEach((button) => {
      button.addEventListener('click', async () => {
        const targetId = button.getAttribute('data-copy-from');
        const box = button.closest('.copyable-box');
        const fromInput = targetId ? document.getElementById(targetId) : null;
        const text = fromInput instanceof HTMLTextAreaElement
          ? fromInput.value
          : (box?.querySelector('.copyable-content')?.textContent ?? '');
        if (!text) return;
        try {
          await navigator.clipboard.writeText(text);
          button.textContent = '✓';
          button.classList.add('copied');
          button.setAttribute('title', 'Copied');
          setTimeout(() => {
            button.textContent = '📋';
            button.classList.remove('copied');
            button.setAttribute('title', 'Copy to clipboard');
          }, 1500);
        } catch {
          vscode.postMessage({ type: 'copy', text });
        }
      });
    });
  </script>
</body>
</html>`;

  return { html };
}

function buildPlanSection(payload: EvaluationJsonPayload): string {
  if (payload.queryPlan) {
    return copyableSection('Query plan (:plan)', payload.queryPlan, 'h2');
  }

  if (payload.queryPlanNote) {
    return `<section><h2>Query plan (:plan)</h2>${copyablePreBlock(payload.queryPlanNote, 'error')}</section>`;
  }

  return '';
}

function formatResultBody(payload: EvaluationJsonPayload): string {
  if (payload.rows && payload.rows.length > 0) {
    const columns = collectColumns(payload.rows);
    const header = columns.map((c) => `<th>${escapeHtml(c)}</th>`).join('');
    const body = payload.rows
      .slice(0, 250)
      .map((row) => `<tr>${columns.map((c) => `<td>${escapeHtml(row[c] ?? '')}</td>`).join('')}</tr>`)
      .join('');

    const truncated = payload.rows.length > 250
      ? `<p class="muted">Showing first 250 of ${payload.rows.length} rows.</p>`
      : '';

    return `<table><thead><tr>${header}</tr></thead><tbody>${body}</tbody></table>${truncated}`;
  }

  if (payload.value === null || payload.value === undefined) {
    return '<p class="muted">&lt;null&gt;</p>';
  }

  return copyablePreBlock(String(payload.value));
}

function copyableSection(title: string, content: string, titleTag: 'h2' | 'h3' = 'h3'): string {
  return `<section>
      <${titleTag}>${escapeHtml(title)}</${titleTag}>
      ${copyablePreBlock(content)}
    </section>`;
}

function copyablePreBlock(content: string, extraClass = ''): string {
  const classNames = ['copyable-content', extraClass].filter(Boolean).join(' ');

  return `<div class="copyable-box">
        <button type="button" class="copy-btn" title="Copy to clipboard" aria-label="Copy to clipboard">📋</button>
        <pre class="${classNames}">${escapeHtml(content)}</pre>
      </div>`;
}

function collectColumns(rows: Array<Record<string, string>>): string[] {
  const columns = new Set<string>();

  for (const row of rows) {
    for (const key of Object.keys(row)) {
      columns.add(key);
    }
  }

  return [...columns].sort();
}

function escapeHtml(value: string): string {
  return value
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;');
}

function getNonce(): string {
  const chars = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789';
  let text = '';

  for (let index = 0; index < 32; index++) {
    text += chars.charAt(Math.floor(Math.random() * chars.length));
  }

  return text;
}

export function formatEvaluationForOutput(
  payload: EvaluationJsonPayload,
  expression: string,
): string {
  const lines: string[] = [`> ${expression}`, ''];

  if (!payload.success) {
    lines.push(`Error: ${payload.error ?? 'Evaluation failed.'}`);
    return lines.join('\n');
  }

  if (payload.value !== undefined && payload.value !== null) {
    lines.push(String(payload.value));
  } else if (payload.rows?.length) {
    lines.push(`${payload.rows.length} row(s)`);
  } else {
    lines.push('<null>');
  }

  lines.push('');
  lines.push(
    `${payload.metrics.totalMs} ms`
      + (payload.metrics.rowCount !== undefined ? ` · ${payload.metrics.rowCount} row(s)` : ''),
  );

  if (payload.sql.length > 0) {
    lines.push('');
    lines.push('SQL:');

    for (const sql of payload.sql) {
      lines.push(sql);
      lines.push('');
    }
  }

  if (payload.queryPlan) {
    lines.push('');
    lines.push('Query plan:');
    lines.push(payload.queryPlan);
  } else if (payload.queryPlanNote) {
    lines.push('');
    lines.push(`Query plan: ${payload.queryPlanNote}`);
  }

  if (payload.warnings.length > 0) {
    lines.push('Warnings:');

    for (const warning of payload.warnings) {
      lines.push(`  - ${warning}`);
    }
  }

  return lines.join('\n');
}
