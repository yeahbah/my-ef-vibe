import * as vscode from 'vscode';
import type { EvaluationHistoryEntry } from './evaluationHistory';

const VIEW_TYPE = 'efvibe.charts';

export class EfvibeChartsPanel {
  private static current: EfvibeChartsPanel | undefined;

  private constructor(private readonly panel: vscode.WebviewPanel) {
    panel.onDidDispose(() => {
      if (EfvibeChartsPanel.current === this) {
        EfvibeChartsPanel.current = undefined;
      }
    });
  }

  static show(
    history: EvaluationHistoryEntry[],
    baseline: EvaluationHistoryEntry | undefined,
    latest: EvaluationHistoryEntry | undefined,
  ): void {
    const html = buildHtml(history, baseline, latest);

    if (EfvibeChartsPanel.current) {
      EfvibeChartsPanel.current.panel.webview.html = html;
      EfvibeChartsPanel.current.panel.reveal(vscode.ViewColumn.Beside);
      return;
    }

    const panel = vscode.window.createWebviewPanel(
      VIEW_TYPE,
      'efvibe Charts',
      { viewColumn: vscode.ViewColumn.Beside, preserveFocus: true },
      { enableScripts: false },
    );

    panel.webview.html = html;
    EfvibeChartsPanel.current = new EfvibeChartsPanel(panel);
  }
}

function buildHtml(
  history: EvaluationHistoryEntry[],
  baseline: EvaluationHistoryEntry | undefined,
  latest: EvaluationHistoryEntry | undefined,
): string {
  const timingRows = history.slice(0, 15).map((entry, index) => {
    const width = Math.min(100, Math.max(4, entry.totalMs));
    return `<tr>
      <td>${index + 1}</td>
      <td><code>${escapeHtml(truncate(entry.expression, 60))}</code></td>
      <td>${entry.totalMs} ms</td>
      <td><div style="width:${width}px" class="bar"></div></td>
    </tr>`;
  }).join('');

  const compareSection = baseline && latest
    ? `<section>
      <h2>Compare (:chart compare)</h2>
      <table>
        <thead><tr><th>Metric</th><th>Baseline</th><th>Latest</th></tr></thead>
        <tbody>
          <tr><td>Total</td><td>${baseline.totalMs} ms</td><td>${latest.totalMs} ms</td></tr>
          <tr><td>Database</td><td>${baseline.databaseMs ?? '-'} ms</td><td>${latest.databaseMs ?? '-'} ms</td></tr>
          <tr><td>Rows</td><td>${baseline.rowCount ?? '-'}</td><td>${latest.rowCount ?? '-'}</td></tr>
          <tr><td>SQL commands</td><td>${baseline.sqlCommandCount}</td><td>${latest.sqlCommandCount}</td></tr>
        </tbody>
      </table>
      <p class="muted">Baseline: <code>${escapeHtml(truncate(baseline.expression, 80))}</code></p>
      <p class="muted">Latest: <code>${escapeHtml(truncate(latest.expression, 80))}</code></p>
    </section>`
    : '<p class="muted">Set a compare baseline with <strong>efvibe: Set Compare Baseline</strong>, then run another query.</p>';

  return `<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8" />
  <style>
    body { font-family: var(--vscode-font-family); color: var(--vscode-foreground); background: var(--vscode-editor-background); padding: 12px 16px; }
    h2 { margin: 0 0 8px; }
    .muted { color: var(--vscode-descriptionForeground); }
    table { border-collapse: collapse; width: 100%; margin-bottom: 20px; }
    th, td { border: 1px solid var(--vscode-panel-border); padding: 4px 8px; text-align: left; vertical-align: middle; }
    th { background: var(--vscode-editor-inactiveSelectionBackground); }
    .bar { display: inline-block; height: 10px; background: var(--vscode-charts-blue, #3794ff); border-radius: 2px; min-width: 4px; }
    code { font-size: 0.85rem; }
    section { margin-bottom: 24px; }
  </style>
</head>
<body>
  <section>
    <h2>Session timings (:chart stats)</h2>
    ${history.length ? `<table>
      <thead><tr><th>#</th><th>Expression</th><th>Total</th><th></th></tr></thead>
      <tbody>${timingRows}</tbody>
    </table>` : '<p class="muted">Run queries from the editor to populate session charts.</p>'}
  </section>
  ${compareSection}
</body>
</html>`;
}

function truncate(value: string, max: number): string {
  const trimmed = value.trim();
  return trimmed.length <= max ? trimmed : `${trimmed.slice(0, max - 1)}…`;
}

function escapeHtml(value: string): string {
  return value.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
}
