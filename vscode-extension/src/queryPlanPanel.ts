import * as vscode from 'vscode';

const VIEW_TYPE = 'efvibe.queryPlan';

export class EfvibeQueryPlanPanel {
  private static current: EfvibeQueryPlanPanel | undefined;

  private constructor(private readonly panel: vscode.WebviewPanel) {
    panel.onDidDispose(() => {
      if (EfvibeQueryPlanPanel.current === this) {
        EfvibeQueryPlanPanel.current = undefined;
      }
    });
  }

  static show(title: string, plan: string, sql?: string): void {
    const html = buildHtml(title, plan, sql);

    if (EfvibeQueryPlanPanel.current) {
      EfvibeQueryPlanPanel.current.panel.title = title;
      EfvibeQueryPlanPanel.current.panel.webview.html = html;
      EfvibeQueryPlanPanel.current.panel.reveal(vscode.ViewColumn.Beside);
      return;
    }

    const panel = vscode.window.createWebviewPanel(
      VIEW_TYPE,
      title,
      { viewColumn: vscode.ViewColumn.Beside, preserveFocus: true },
      { enableScripts: false },
    );

    panel.webview.html = html;
    EfvibeQueryPlanPanel.current = new EfvibeQueryPlanPanel(panel);
  }
}

function buildHtml(title: string, plan: string, sql?: string): string {
  const sqlSection = sql?.trim()
    ? `<section><h2>SQL</h2><pre>${escapeHtml(sql.trim())}</pre></section>`
    : '';

  return `<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8" />
  <style>
    body { font-family: var(--vscode-font-family); color: var(--vscode-foreground); background: var(--vscode-editor-background); padding: 12px 16px; }
    h2 { margin: 0 0 8px; }
    pre { background: var(--vscode-textCodeBlock-background); padding: 10px; overflow: auto; white-space: pre-wrap; border: 1px solid var(--vscode-panel-border); border-radius: 4px; }
    section { margin-bottom: 20px; }
  </style>
</head>
<body>
  <section>
    <h2>${escapeHtml(title)}</h2>
    <pre>${escapeHtml(plan.trim())}</pre>
  </section>
  ${sqlSection}
</body>
</html>`;
}

function escapeHtml(value: string): string {
  return value.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
}
