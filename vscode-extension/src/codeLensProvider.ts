import * as vscode from 'vscode';
import { findStatementStart } from './expressionSelection';

/** Line looks like part of an EF LINQ chain or query terminal. */
const QUERY_LINE = /\b(?:await\s+)?(?:db|DbContext)\.[\w.]+|\.(?:Where|Include|ThenInclude|FirstOrDefaultAsync|ToListAsync|Select)\s*\(/u;

export function registerEfvibeCodeLens(context: vscode.ExtensionContext): void {
  context.subscriptions.push(
    vscode.languages.registerCodeLensProvider(
      { language: 'csharp', scheme: 'file' },
      {
        provideCodeLenses(document) {
          if (!vscode.workspace.getConfiguration('efvibe').get<boolean>('codeLens.enabled', true)) {
            return undefined;
          }

          const lenses: vscode.CodeLens[] = [];
          const statementStartsWithLens = new Set<number>();

          for (let line = 0; line < document.lineCount; line += 1) {
            const text = document.lineAt(line).text;

            if (!QUERY_LINE.test(text)) {
              continue;
            }

            const startLine = findStatementStart(document, line);

            if (startLine !== line || statementStartsWithLens.has(startLine)) {
              continue;
            }

            statementStartsWithLens.add(startLine);

            const range = new vscode.Range(startLine, 0, startLine, 0);
            lenses.push(new vscode.CodeLens(range, {
              title: 'Run with efvibe',
              command: 'efvibe.runStatementAtCursor',
              arguments: [],
            }));
          }

          return lenses;
        },
      },
    ),
  );
}
