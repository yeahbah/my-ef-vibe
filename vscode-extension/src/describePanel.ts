import * as vscode from 'vscode';
import type { DescribeJsonPayload } from './cliRunner';

const VIEW_TYPE = 'efvibe.describe';

export class EfvibeDescribePanel {
  private static current: EfvibeDescribePanel | undefined;

  private constructor(private readonly panel: vscode.WebviewPanel) {
    panel.onDidDispose(() => {
      if (EfvibeDescribePanel.current === this) {
        EfvibeDescribePanel.current = undefined;
      }
    });
  }

  static show(context: vscode.ExtensionContext, payload: DescribeJsonPayload): void {
    const html = buildHtml(payload);
    const title = payload.success && payload.dbSet
      ? `efvibe: ${payload.dbSet}`
      : 'efvibe: Describe';

    if (EfvibeDescribePanel.current) {
      EfvibeDescribePanel.current.panel.title = title;
      EfvibeDescribePanel.current.panel.webview.html = html;
      EfvibeDescribePanel.current.panel.reveal(vscode.ViewColumn.Beside);
      return;
    }

    const panel = vscode.window.createWebviewPanel(
      VIEW_TYPE,
      title,
      { viewColumn: vscode.ViewColumn.Beside, preserveFocus: true },
      {
        enableScripts: false,
        retainContextWhenHidden: true,
        localResourceRoots: [context.extensionUri],
      },
    );

    panel.webview.html = html;
    EfvibeDescribePanel.current = new EfvibeDescribePanel(panel);
  }
}

function buildHtml(payload: DescribeJsonPayload): string {
  if (!payload.success) {
    const known = payload.knownEntities?.length
      ? `<ul>${payload.knownEntities.map((entry) => `<li>${escapeHtml(entry)}</li>`).join('')}</ul>`
      : '';

    return wrapHtml(
      payload.dbSet ?? 'Describe',
      `<p class="error">${escapeHtml(payload.error ?? 'Describe failed.')}</p>${known}`,
    );
  }

  const header = `<p class="muted">${escapeHtml(payload.entityTypeFullName ?? payload.entityType ?? '')}</p>`;
  const rows = (payload.members ?? [])
    .map((member) => `<tr>
      <td>${escapeHtml(member.name)}</td>
      <td>${escapeHtml(member.type)}</td>
      <td>${escapeHtml(member.nullable)}</td>
      <td>${escapeHtml(member.notes ?? '')}</td>
    </tr>`)
    .join('');

  const table = rows
    ? `<table>
      <thead><tr><th>Member</th><th>Type</th><th>Nullable</th><th>Notes</th></tr></thead>
      <tbody>${rows}</tbody>
    </table>`
    : '<p class="muted">No members found.</p>';

  return wrapHtml(payload.dbSet ?? 'Describe', `${header}${table}`);
}

function wrapHtml(title: string, body: string): string {
  return `<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8" />
  <style>
    body { font-family: var(--vscode-font-family); color: var(--vscode-foreground); background: var(--vscode-editor-background); padding: 12px 16px; margin: 0; }
    h2 { font-weight: 600; margin: 0 0 12px; }
    .muted { color: var(--vscode-descriptionForeground); margin: 0 0 12px; }
    .error { color: var(--vscode-errorForeground); }
    table { border-collapse: collapse; width: 100%; }
    th, td { border: 1px solid var(--vscode-panel-border); padding: 4px 8px; text-align: left; vertical-align: top; }
    th { background: var(--vscode-editor-inactiveSelectionBackground); }
  </style>
</head>
<body>
  <h2>${escapeHtml(title)}</h2>
  ${body}
</body>
</html>`;
}

function escapeHtml(value: string): string {
  return value
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;');
}
