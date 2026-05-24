import * as vscode from 'vscode';
import { runTablesJson } from './cliRunner';
import { getSearchDirectory, getWorkspaceFolder, readSettings } from './config';

export async function pickEntityCommand(): Promise<{
  dbSet: string;
  entityType: string;
  entityTypeFullName?: string;
} | undefined> {
  const folder = getWorkspaceFolder();

  if (!folder) {
    void vscode.window.showWarningMessage('Open a workspace folder before picking an entity.');
    return undefined;
  }

  const settings = readSettings(folder);

  if (!settings.project) {
    void vscode.window.showWarningMessage('Set efvibe.project in settings.');
    return undefined;
  }

  const searchDirectory = getSearchDirectory(settings, folder);
  const tables = await runTablesJson(settings, searchDirectory, folder.uri.fsPath);

  if (!tables?.tables.length) {
    void vscode.window.showWarningMessage('Could not load DbSets for entity picker.');
    return undefined;
  }

  const picked = await vscode.window.showQuickPick(
    tables.tables.map((entry) => ({
      label: entry.dbSet,
      description: entry.entityType,
      detail: entry.entityTypeFullName,
      entry,
    })),
    { title: 'efvibe: Pick entity', placeHolder: 'DbSet or entity type' },
  );

  return picked?.entry;
}
