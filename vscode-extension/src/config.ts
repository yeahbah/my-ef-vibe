import * as path from 'path';
import * as vscode from 'vscode';
import { getDefaultWorkspaceRoot } from './sessionPaths';

export type EfvibeResultDestination = 'panel' | 'output' | 'terminal';

export interface EfvibeSettings {
  workspaceRoot: string;
  project: string;
  startupProject: string;
  context: string;
  connectionString: string;
  provider: string;
  toolPath: string;
  dbLog: boolean;
  dotnetFramework: string;
  resultDestination: EfvibeResultDestination;
}

export function getWorkspaceFolder(): vscode.WorkspaceFolder | undefined {
  return vscode.workspace.workspaceFolders?.[0];
}

export function resolveSettingPath(value: string, workspaceFolder?: vscode.WorkspaceFolder): string {
  if (!value.trim()) {
    return '';
  }

  const folderPath = workspaceFolder?.uri.fsPath ?? '';
  return value.replace(/\$\{workspaceFolder\}/g, folderPath).trim();
}

export function readSettings(workspaceFolder?: vscode.WorkspaceFolder): EfvibeSettings {
  const config = vscode.workspace.getConfiguration('efvibe', workspaceFolder?.uri);
  const folder = workspaceFolder ?? getWorkspaceFolder();

  const workspaceRootRaw = config.get<string>('workspaceRoot', '').trim();
  const workspaceRoot = workspaceRootRaw
    ? resolveSettingPath(workspaceRootRaw, folder)
    : getDefaultWorkspaceRoot();

  return {
    workspaceRoot,
    project: resolveSettingPath(config.get<string>('project', ''), folder),
    startupProject: resolveSettingPath(config.get<string>('startupProject', ''), folder),
    context: config.get<string>('context', '').trim(),
    connectionString: config.get<string>('connectionString', '').trim(),
    provider: config.get<string>('provider', '').trim(),
    toolPath: resolveSettingPath(config.get<string>('toolPath', ''), folder),
    dbLog: config.get<boolean>('dbLog', config.get<boolean>('showSql', true)),
    dotnetFramework: config.get<string>('dotnetFramework', '').trim(),
    resultDestination: config.get<EfvibeResultDestination>('resultDestination', 'panel'),
  };
}

export function getSearchDirectory(settings: EfvibeSettings, workspaceFolder?: vscode.WorkspaceFolder): string {
  const folder = workspaceFolder ?? getWorkspaceFolder();
  if (folder) {
    return folder.uri.fsPath;
  }

  if (settings.project) {
    return path.dirname(settings.project);
  }

  return settings.workspaceRoot;
}
