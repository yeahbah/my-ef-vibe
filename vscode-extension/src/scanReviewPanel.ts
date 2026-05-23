import * as vscode from 'vscode';
import type { ScanReviewItem } from './scanReviewTypes';

const VIEW_TYPE = 'efvibe.scanReview';

export interface ScanReviewPanelHandlers {
  onDismiss: (item: ScanReviewItem, dismissalNote?: string) => Promise<void>;
  onSaveNote: (item: ScanReviewItem, note: string) => Promise<void>;
  onGoToSource: (item: ScanReviewItem) => Promise<void>;
}

export class EfvibeScanReviewPanel {
  private static current: EfvibeScanReviewPanel | undefined;

  private readonly panel: vscode.WebviewPanel;

  private handlers: ScanReviewPanelHandlers;

  private items: ScanReviewItem[];

  private index: number;

  private constructor(
    panel: vscode.WebviewPanel,
    items: ScanReviewItem[],
    startIndex: number,
    handlers: ScanReviewPanelHandlers,
  ) {
    this.panel = panel;
    this.handlers = handlers;
    this.items = items;
    this.index = Math.min(Math.max(0, startIndex), Math.max(0, items.length - 1));

    panel.webview.onDidReceiveMessage(async (message: {
      type?: string;
      index?: number;
      note?: string;
      dismissalNote?: string;
      text?: string;
    }) => {
      const current = this.items[this.index];

      if (!current && message.type !== 'prev' && message.type !== 'next') {
        return;
      }

      switch (message.type) {
        case 'prev':
          this.navigate(-1);
          break;
        case 'next':
          this.navigate(1);
          break;
        case 'goToSource':
          if (current) {
            await this.handlers.onGoToSource(current);
          }
          break;
        case 'dismiss':
          if (current) {
            await this.handlers.onDismiss(
              current,
              typeof message.dismissalNote === 'string' ? message.dismissalNote : undefined,
            );
          }
          break;
        case 'saveNote':
          if (current && typeof message.note === 'string') {
            await this.handlers.onSaveNote(current, message.note);
          }
          break;
        case 'copy':
          if (typeof message.text === 'string') {
            await vscode.env.clipboard.writeText(message.text);
            void vscode.window.setStatusBarMessage('efvibe: Copied to clipboard.', 2000);
          }
          break;
        default:
          break;
      }
    });

    panel.onDidDispose(() => {
      if (EfvibeScanReviewPanel.current === this) {
        EfvibeScanReviewPanel.current = undefined;
      }
    });
  }

  static show(
    context: vscode.ExtensionContext,
    items: ScanReviewItem[],
    startIndex: number,
    handlers: ScanReviewPanelHandlers,
  ): void {
    const showIn = resolveSplitViewColumn();
    const html = buildHtml(items, startIndex);

    if (EfvibeScanReviewPanel.current) {
      EfvibeScanReviewPanel.current.handlers = handlers;
      EfvibeScanReviewPanel.current.setItems(items, startIndex);

      if (typeof showIn === 'number') {
        EfvibeScanReviewPanel.current.panel.reveal(showIn);
      } else {
        EfvibeScanReviewPanel.current.panel.reveal(showIn.viewColumn, showIn.preserveFocus);
      }

      return;
    }

    const panel = vscode.window.createWebviewPanel(
      VIEW_TYPE,
      'My EF Vibe scan',
      showIn,
      {
        enableScripts: true,
        retainContextWhenHidden: true,
        localResourceRoots: [context.extensionUri],
      },
    );

    const instance = new EfvibeScanReviewPanel(panel, items, startIndex, handlers);
    panel.webview.html = html;
    EfvibeScanReviewPanel.current = instance;
  }

  setItems(items: ScanReviewItem[], startIndex: number): void {
    this.items = items;
    this.index = items.length === 0 ? 0 : Math.min(Math.max(0, startIndex), items.length - 1);
    this.render();
  }

  private navigate(delta: number): void {
    if (this.items.length === 0) {
      return;
    }

    this.index = (this.index + delta + this.items.length) % this.items.length;
    this.render();
  }

  private render(): void {
    this.panel.webview.html = buildHtml(this.items, this.index);
    this.panel.title = this.items.length === 0
      ? 'My EF Vibe scan'
      : `My EF Vibe scan (${this.index + 1}/${this.items.length})`;
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

function buildHtml(items: ScanReviewItem[], index: number): string {
  const nonce = getNonce();

  if (items.length === 0) {
    return wrapPage(
      nonce,
      '<p class="muted">No scan findings. Run <strong>Scan Workspace</strong> or refresh after a REPL <code>:scan</code>.</p>',
      '',
    );
  }

  const safeIndex = Math.min(Math.max(0, index), items.length - 1);
  const item = items[safeIndex];
  const finding = item.finding;
  const severity = (finding.severity ?? 'warning').toLowerCase();
  const locationLabel = `${finding.filePath} · line ${finding.line}`;
  const codeBlock = finding.code?.trim()
    ? copyableSection('Code', finding.code.trim())
    : '';
  const recommendation = finding.recommendation?.trim()
    ? `<section><h3>Recommendation</h3><p>${escapeHtml(finding.recommendation.trim())}</p></section>`
    : '';
  const sql = finding.translatedSql?.trim()
    ? copyableSection('Translated SQL', finding.translatedSql.trim())
    : '';
  const sqlNote = finding.sqlTranslationNote?.trim()
    ? `<p class="muted">${escapeHtml(finding.sqlTranslationNote.trim())}</p>`
    : '';
  const plan = finding.queryPlan?.trim()
    ? copyableSection('Query plan', finding.queryPlan.trim())
    : '';
  const savedNote = finding.savedNote?.trim() ?? '';

  const body = `
    <header class="carousel-header">
      <p class="counter">Finding <strong>${safeIndex + 1}</strong> of <strong>${items.length}</strong></p>
      <div class="badges">
        <span class="badge rule">${escapeHtml(finding.ruleId)}</span>
        <span class="badge severity ${escapeHtml(severity)}">${escapeHtml(severity)}</span>
        <span class="badge mode">${escapeHtml(item.scanMode)}</span>
      </div>
    </header>
    <section>
      <h3>Location</h3>
      <p>
        <button type="button" class="location-link go-to-source" title="Open in editor">
          ${escapeHtml(locationLabel)}
        </button>
      </p>
    </section>
    <section>
      <h3>Message</h3>
      <p>${escapeHtml(finding.message)}</p>
    </section>
    ${recommendation}
    ${codeBlock}
    ${sql}
    ${sqlNote}
    ${plan}
    <section class="note-section">
      <h3>Note</h3>
      <textarea id="note" spellcheck="true" placeholder="Team note for this finding (saved to myefvibe-scan-notes.json)">${escapeHtml(savedNote)}</textarea>
    </section>
  `;

  const toolbar = `
    <div class="toolbar">
      <button class="secondary" id="prev" ${items.length < 2 ? 'disabled' : ''}>← Previous</button>
      <button class="secondary" id="next" ${items.length < 2 ? 'disabled' : ''}>Next →</button>
      <button class="secondary" id="goToSource">Go to code</button>
      <button class="secondary" id="saveNote">Save note</button>
      <button class="primary" id="dismiss">Dismiss</button>
    </div>
    <p class="hint">Dismiss hides this finding on the next scan when <code>efvibe.scan.respectDismissals</code> is enabled. Use arrow keys ← → when the panel is focused.</p>
  `;

  return wrapPage(nonce, body, toolbar);
}

function wrapPage(nonce: string, body: string, toolbar: string): string {
  return `<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8" />
  <meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; script-src 'nonce-${nonce}';" />
  <style>
    body { font-family: var(--vscode-font-family); color: var(--vscode-foreground); background: var(--vscode-editor-background); padding: 12px 16px 24px; margin: 0; }
    h3 { font-weight: 600; margin: 0 0 8px; font-size: 0.95rem; }
    pre { background: var(--vscode-textCodeBlock-background); overflow: auto; white-space: pre-wrap; border-radius: 4px; margin: 0; font-size: 0.85rem; }
    .copyable-box { position: relative; margin: 0; }
    .copyable-box pre.copyable-content {
      padding: 32px 12px 10px 10px;
      border: 1px solid var(--vscode-panel-border);
      box-sizing: border-box;
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
    }
    .copy-btn:hover { background: var(--vscode-list-hoverBackground); }
    .copy-btn.copied {
      color: var(--vscode-testing-iconPassed, var(--vscode-gitDecoration-addedResourceForeground));
      border-color: var(--vscode-testing-iconPassed, var(--vscode-gitDecoration-addedResourceForeground));
    }
    .muted { color: var(--vscode-descriptionForeground); }
    .carousel-header { margin-bottom: 16px; }
    .counter { margin: 0 0 8px; font-size: 1.1rem; }
    .badges { display: flex; flex-wrap: wrap; gap: 6px; }
    .badge { font-size: 0.75rem; padding: 2px 8px; border-radius: 999px; border: 1px solid var(--vscode-panel-border); }
    .badge.rule { font-family: var(--vscode-editor-font-family); }
    .badge.severity.error, .badge.severity.critical { border-color: var(--vscode-errorForeground); color: var(--vscode-errorForeground); }
    .badge.severity.warning { border-color: var(--vscode-editorWarning-foreground); color: var(--vscode-editorWarning-foreground); }
  section { margin-bottom: 16px; }
    #note { width: 100%; min-height: 4rem; box-sizing: border-box; font-family: var(--vscode-font-family); font-size: 0.9rem; color: var(--vscode-editor-foreground); background: var(--vscode-input-background); border: 1px solid var(--vscode-input-border, var(--vscode-panel-border)); border-radius: 4px; padding: 8px 10px; resize: vertical; }
    .toolbar { display: flex; flex-wrap: wrap; gap: 8px; margin-top: 20px; align-items: center; }
    button { font-family: var(--vscode-font-family); font-size: 0.9rem; padding: 6px 14px; border-radius: 4px; border: 1px solid var(--vscode-button-border, transparent); cursor: pointer; }
    button.primary { background: var(--vscode-button-background); color: var(--vscode-button-foreground); }
    button.secondary { background: var(--vscode-button-secondaryBackground); color: var(--vscode-button-secondaryForeground); }
    button:disabled { opacity: 0.55; cursor: default; }
    .location-link {
      background: none;
      border: none;
      padding: 0;
      font-family: var(--vscode-editor-font-family);
      font-size: 0.9rem;
      color: var(--vscode-textLink-foreground);
      cursor: pointer;
      text-decoration: underline;
      text-align: left;
      white-space: normal;
      word-break: break-all;
    }
    .location-link:hover { color: var(--vscode-textLink-activeForeground); }
    .hint { color: var(--vscode-descriptionForeground); font-size: 0.85rem; margin-top: 10px; }
  </style>
</head>
<body>
  ${body}
  ${toolbar}
  <script nonce="${nonce}">
    const vscode = acquireVsCodeApi();
    const noteEl = document.getElementById('note');
    document.getElementById('prev')?.addEventListener('click', () => vscode.postMessage({ type: 'prev' }));
    document.getElementById('next')?.addEventListener('click', () => vscode.postMessage({ type: 'next' }));
    function goToSource() { vscode.postMessage({ type: 'goToSource' }); }
    document.getElementById('goToSource')?.addEventListener('click', goToSource);
    document.querySelectorAll('.go-to-source').forEach((el) => el.addEventListener('click', goToSource));
    document.getElementById('saveNote')?.addEventListener('click', () => {
      vscode.postMessage({ type: 'saveNote', note: noteEl ? noteEl.value : '' });
    });
    document.getElementById('dismiss')?.addEventListener('click', () => {
      const dismissalNote = noteEl && noteEl.value.trim() ? noteEl.value.trim() : undefined;
      vscode.postMessage({ type: 'dismiss', dismissalNote });
    });
    document.addEventListener('keydown', (event) => {
      if (event.key === 'ArrowLeft') { event.preventDefault(); vscode.postMessage({ type: 'prev' }); }
      if (event.key === 'ArrowRight') { event.preventDefault(); vscode.postMessage({ type: 'next' }); }
    });
    document.querySelectorAll('.copy-btn').forEach((button) => {
      button.addEventListener('click', async () => {
        const box = button.closest('.copyable-box');
        const content = box?.querySelector('.copyable-content');
        const text = content?.textContent ?? '';
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
}

function copyableSection(title: string, content: string): string {
  return `<section>
      <h3>${escapeHtml(title)}</h3>
      <div class="copyable-box">
        <button type="button" class="copy-btn" title="Copy to clipboard" aria-label="Copy to clipboard">📋</button>
        <pre class="copyable-content">${escapeHtml(content)}</pre>
      </div>
    </section>`;
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
