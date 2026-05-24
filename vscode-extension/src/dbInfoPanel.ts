import * as vscode from 'vscode';
import type { DbInfoJsonPayload } from './cliRunner';

const VIEW_TYPE = 'efvibe.dbinfo';

export class EfvibeDbInfoPanel {
  private static current: EfvibeDbInfoPanel | undefined;

  private constructor(private readonly panel: vscode.WebviewPanel) {
    panel.onDidDispose(() => {
      if (EfvibeDbInfoPanel.current === this) {
        EfvibeDbInfoPanel.current = undefined;
      }
    });
  }

  static show(payload: DbInfoJsonPayload): void {
    const html = buildHtml(payload);

    if (EfvibeDbInfoPanel.current) {
      EfvibeDbInfoPanel.current.panel.webview.html = html;
      EfvibeDbInfoPanel.current.panel.reveal(vscode.ViewColumn.Beside);
      return;
    }

    const panel = vscode.window.createWebviewPanel(
      VIEW_TYPE,
      `efvibe: ${payload.dbContext}`,
      { viewColumn: vscode.ViewColumn.Beside, preserveFocus: true },
      { enableScripts: false },
    );

    panel.webview.html = html;
    EfvibeDbInfoPanel.current = new EfvibeDbInfoPanel(panel);
  }
}

function buildHtml(payload: DbInfoJsonPayload): string {
  const rows = payload.entries
    .map((entry) => `<tr><td>${escapeHtml(entry.key)}</td><td>${escapeHtml(entry.value ?? '')}</td></tr>`)
    .join('');

  return `<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8" />
  <style>
    body { font-family: var(--vscode-font-family); color: var(--vscode-foreground); background: var(--vscode-editor-background); padding: 12px 16px; }
    h2 { margin: 0 0 12px; }
    table { border-collapse: collapse; width: 100%; }
    th, td { border: 1px solid var(--vscode-panel-border); padding: 4px 8px; text-align: left; vertical-align: top; }
    th { background: var(--vscode-editor-inactiveSelectionBackground); }
  </style>
</head>
<body>
  <h2>:dbinfo</h2>
  <table>
    <thead><tr><th>Key</th><th>Value</th></tr></thead>
    <tbody>${rows}</tbody>
  </table>
</body>
</html>`;
}

function escapeHtml(value: string): string {
  return value.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
}
