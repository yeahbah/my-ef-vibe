import * as vscode from 'vscode';

export type ExpressionSelectionKind = 'selection' | 'line' | 'statement';

export function getExpressionFromEditor(
  editor: vscode.TextEditor,
  kind: ExpressionSelectionKind,
): string | undefined {
  const document = editor.document;

  if (kind === 'selection') {
    const text = document.getText(editor.selection).trim();
    return text.length > 0 ? text : undefined;
  }

  const line = kind === 'line'
    ? editor.selection.active.line
    : editor.selection.active.line;

  const lineText = document.lineAt(line).text;
  const trimmed = lineText.trim();

  if (!trimmed) {
    return undefined;
  }

  if (kind === 'line') {
    return trimmed;
  }

  return expandStatementAtLine(document, line) ?? trimmed;
}

function expandStatementAtLine(document: vscode.TextDocument, line: number): string | undefined {
  const startLine = findStatementStart(document, line);
  const endLine = findStatementEnd(document, line);
  const parts: string[] = [];

  for (let index = startLine; index <= endLine; index++) {
    parts.push(document.lineAt(index).text);
  }

  const combined = parts.join('\n').trim();
  return combined.length > 0 ? combined : undefined;
}

export function findStatementStart(document: vscode.TextDocument, line: number): number {
  for (let index = line; index >= 0; index--) {
    const text = document.lineAt(index).text.trim();

    if (index < line && isBoundaryLine(text)) {
      return index + 1;
    }
  }

  return 0;
}

function findStatementEnd(document: vscode.TextDocument, line: number): number {
  const lastLine = document.lineCount - 1;

  for (let index = line; index <= lastLine; index++) {
    const text = document.lineAt(index).text.trim();

    if (index > line && isBoundaryLine(text)) {
      return index - 1;
    }
  }

  return lastLine;
}

function isBoundaryLine(text: string): boolean {
  if (!text) {
    return true;
  }

  return /^(public|private|protected|internal|static|class|interface|record|enum|namespace|#region|#endregion|\[|\}|\{)/u.test(text);
}
