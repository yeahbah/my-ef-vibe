import * as vscode from 'vscode';
import { runCompletionsJson, type CompletionJsonItem } from './cliRunner';
import { getSearchDirectory, getWorkspaceFolder, readSettings } from './config';

let cachedItems: CompletionJsonItem[] | undefined;
let cacheKey = '';

export function registerEfvibeCompletion(context: vscode.ExtensionContext): void {
  context.subscriptions.push(
    vscode.languages.registerCompletionItemProvider(
      { language: 'csharp', scheme: 'file' },
      {
        provideCompletionItems: async (document, position) => {
          if (!vscode.workspace.getConfiguration('efvibe').get<boolean>('completion.enabled', true)) {
            return undefined;
          }

          const prefix = readDbPrefix(document, position);

          if (!prefix) {
            return undefined;
          }

          const items = await loadCompletions(prefix);

          return items.map((item) => {
            const completion = new vscode.CompletionItem(item.label, mapKind(item.kind));
            completion.insertText = item.insertText;
            completion.detail = item.detail;
            completion.sortText = item.label.toLowerCase();
            return completion;
          });
        },
      },
      '.',
    ),
  );
}

function readDbPrefix(document: vscode.TextDocument, position: vscode.Position): string | undefined {
  const line = document.lineAt(position.line).text.slice(0, position.character);
  const match = /(?:^|[^\w])((?:db)(?:\.[\w]*)*)$/u.exec(line);

  return match?.[1];
}

async function loadCompletions(prefix: string): Promise<CompletionJsonItem[]> {
  const folder = getWorkspaceFolder();

  if (!folder) {
    return [];
  }

  const settings = readSettings(folder);

  if (!settings.project) {
    return [];
  }

  const key = `${settings.project}|${settings.context}|${prefix}`;

  if (cacheKey === key && cachedItems) {
    return cachedItems;
  }

  const searchDirectory = getSearchDirectory(settings, folder);
  const payload = await runCompletionsJson(settings, searchDirectory, folder.uri.fsPath, prefix);
  cachedItems = payload?.items ?? [];
  cacheKey = key;

  return cachedItems;
}

function mapKind(kind: string): vscode.CompletionItemKind {
  return kind === 'method'
    ? vscode.CompletionItemKind.Method
    : vscode.CompletionItemKind.Property;
}

export function invalidateCompletionCache(): void {
  cachedItems = undefined;
  cacheKey = '';
}
