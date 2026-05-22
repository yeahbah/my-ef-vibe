import * as fs from 'fs';
import * as path from 'path';
import * as vscode from 'vscode';
import { buildReplCommand } from './cliRunner';
import type { EfvibeSettings } from './config';

const TASK_LABEL = 'efvibe: Start REPL';

export async function generateReplTask(
  settings: EfvibeSettings,
  searchDirectory: string,
  workspaceFolder: vscode.WorkspaceFolder,
): Promise<void> {
  const vscodeDir = path.join(workspaceFolder.uri.fsPath, '.vscode');
  const tasksPath = path.join(vscodeDir, 'tasks.json');
  const commandLine = buildReplCommand(settings, searchDirectory);

  const task = {
    label: TASK_LABEL,
    type: 'shell',
    command: commandLine,
    options: { cwd: workspaceFolder.uri.fsPath },
    presentation: {
      reveal: 'always',
      panel: 'dedicated',
      focus: true,
    },
    problemMatcher: [],
  };

  let document: {
    version?: string;
    tasks?: Array<Record<string, unknown>>;
  };

  if (fs.existsSync(tasksPath)) {
    try {
      document = JSON.parse(fs.readFileSync(tasksPath, 'utf8')) as typeof document;
    } catch {
      vscode.window.showErrorMessage('Could not parse .vscode/tasks.json. Fix it manually and try again.');
      return;
    }
  } else {
    await fs.promises.mkdir(vscodeDir, { recursive: true });
    document = { version: '2.0.0', tasks: [] };
  }

  document.version ??= '2.0.0';
  document.tasks ??= [];

  const existingIndex = document.tasks.findIndex((entry) => entry.label === TASK_LABEL);

  if (existingIndex >= 0) {
    document.tasks[existingIndex] = task;
  } else {
    document.tasks.push(task);
  }

  await fs.promises.writeFile(tasksPath, `${JSON.stringify(document, null, 2)}\n`, 'utf8');

  const open = await vscode.window.showInformationMessage(
    `Updated ${TASK_LABEL} in .vscode/tasks.json.`,
    'Open tasks.json',
  );

  if (open === 'Open tasks.json') {
    const doc = await vscode.workspace.openTextDocument(tasksPath);
    await vscode.window.showTextDocument(doc);
  }
}
