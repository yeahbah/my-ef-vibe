import * as vscode from 'vscode';
import type { EvaluationJsonPayload } from './evaluationTypes';

const VIEW_TYPE = 'efvibe.result';

export class EfvibeResultPanel {
  private static current: EfvibeResultPanel | undefined;

  private readonly panel: vscode.WebviewPanel;

  private constructor(panel: vscode.WebviewPanel) {
    this.panel = panel;
    panel.onDidDispose(() => {
      if (EfvibeResultPanel.current === this) {
        EfvibeResultPanel.current = undefined;
      }
    });
  }

  static show(payload: EvaluationJsonPayload, expression: string): void {
    const showIn = resolveSplitViewColumn();

    if (EfvibeResultPanel.current) {
      if (typeof showIn === 'number') {
        EfvibeResultPanel.current.panel.reveal(showIn);
      } else {
        EfvibeResultPanel.current.panel.reveal(showIn.viewColumn, showIn.preserveFocus);
      }
      EfvibeResultPanel.current.panel.webview.html = buildHtml(payload, expression);
      return;
    }

    const panel = vscode.window.createWebviewPanel(
      VIEW_TYPE,
      'efvibe result',
      showIn,
      { enableScripts: false, retainContextWhenHidden: true },
    );

    EfvibeResultPanel.current = new EfvibeResultPanel(panel);
    panel.webview.html = buildHtml(payload, expression);
  }
}

/** Open beside the active editor (split tab), keeping keyboard focus in the editor. */
function resolveSplitViewColumn(): vscode.ViewColumn | {
  viewColumn: vscode.ViewColumn;
  preserveFocus?: boolean;
} {
  if (vscode.window.activeTextEditor) {
    return { viewColumn: vscode.ViewColumn.Beside, preserveFocus: true };
  }

  return vscode.ViewColumn.Active;
}

function buildHtml(payload: EvaluationJsonPayload, expression: string): string {
  const resultSection = payload.success
    ? formatResultBody(payload)
    : `<pre class="error">${escapeHtml(payload.error ?? 'Evaluation failed.')}</pre>`;

  const sqlBlocks = payload.sql.length > 0
    ? payload.sql.map((sql, index) => {
        const title = payload.sql.length > 1 ? `SQL ${index + 1}` : 'SQL';
        return `<section><h3>${title}</h3><pre>${escapeHtml(sql)}</pre></section>`;
      }).join('')
    : '<p class="muted">No SQL captured for this run.</p>';

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

  return `<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8" />
  <meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline';" />
  <style>
    body { font-family: var(--vscode-font-family); color: var(--vscode-foreground); background: var(--vscode-editor-background); padding: 12px 16px; }
    h2, h3 { font-weight: 600; margin: 0 0 8px; }
    pre { background: var(--vscode-textCodeBlock-background); padding: 10px; overflow: auto; white-space: pre-wrap; border-radius: 4px; }
    .muted { color: var(--vscode-descriptionForeground); }
    .error { color: var(--vscode-errorForeground); }
    .expr { margin-bottom: 16px; }
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
    <pre>${escapeHtml(expression)}</pre>
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
  ${warnings}
</body>
</html>`;
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

  return `<pre>${escapeHtml(payload.value)}</pre>`;
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

  if (payload.warnings.length > 0) {
    lines.push('Warnings:');

    for (const warning of payload.warnings) {
      lines.push(`  - ${warning}`);
    }
  }

  return lines.join('\n');
}
