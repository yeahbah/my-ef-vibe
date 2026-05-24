import * as fs from 'fs';
import * as path from 'path';
import * as vscode from 'vscode';
import { getWorkspaceFolder, readSettings } from './config';
import { EfvibeModelTreeItem, EfvibeModelTreeProvider } from './modelTree';
import {
  getDbContextSessionDirectory,
  getLiteScanFilePath,
} from './sessionPaths';

type SessionNodeKind = 'folder' | 'file';

export class EfvibeSessionTreeItem extends vscode.TreeItem {
  constructor(
    public readonly kind: SessionNodeKind,
    label: string,
    resourceUri?: vscode.Uri,
  ) {
    super(
      label,
      kind === 'folder'
        ? vscode.TreeItemCollapsibleState.Collapsed
        : vscode.TreeItemCollapsibleState.None,
    );

    if (kind === 'folder' && resourceUri) {
      this.resourceUri = resourceUri;
      this.iconPath = new vscode.ThemeIcon('folder');
    } else if (kind === 'file' && resourceUri) {
      this.resourceUri = resourceUri;
      this.command = {
        command: 'vscode.open',
        title: 'Open',
        arguments: [resourceUri],
      };
      this.iconPath = new vscode.ThemeIcon('file');
    } else {
      this.iconPath = new vscode.ThemeIcon('folder');
    }
  }
}

export type EfvibeSessionTreeElement = EfvibeModelTreeItem | EfvibeSessionTreeItem;

export class EfvibeSessionTreeProvider implements vscode.TreeDataProvider<EfvibeSessionTreeElement> {
  private readonly changeEmitter = new vscode.EventEmitter<void>();

  readonly onDidChangeTreeData = this.changeEmitter.event;

  readonly model = new EfvibeModelTreeProvider();

  refresh(): void {
    this.changeEmitter.fire();
    this.model.refresh();
  }

  getTreeItem(element: EfvibeSessionTreeElement): vscode.TreeItem {
    return element;
  }

  async getChildren(element?: EfvibeSessionTreeElement): Promise<EfvibeSessionTreeElement[]> {
    if (element instanceof EfvibeModelTreeItem) {
      return this.model.getChildren(element);
    }

    if (element instanceof EfvibeSessionTreeItem) {
      if (element.kind === 'file') {
        return [];
      }

      return listDirectory(element.resourceUri?.fsPath);
    }

    const modelRoots = await this.model.getChildren();
    const sessionRoots = await this.getSessionRoots();
    return [...modelRoots, ...sessionRoots];
  }

  private async getSessionRoots(): Promise<EfvibeSessionTreeItem[]> {
    const folder = getWorkspaceFolder();

    if (!folder) {
      return [];
    }

    const settings = readSettings(folder);

    if (!settings.project) {
      return [];
    }

    const roots: EfvibeSessionTreeItem[] = [];
    const projectScan = path.dirname(getLiteScanFilePath(settings.workspaceRoot, settings.project));

    if (fs.existsSync(projectScan)) {
      roots.push(new EfvibeSessionTreeItem('folder', 'scan', vscode.Uri.file(projectScan)));
    }

    if (settings.context.trim()) {
      const contextDir = getDbContextSessionDirectory(
        settings.workspaceRoot,
        settings.project,
        settings.context.trim(),
      );

      if (fs.existsSync(contextDir)) {
        roots.push(new EfvibeSessionTreeItem('folder', settings.context.trim(), vscode.Uri.file(contextDir)));
      }
    }

    if (roots.length) {
      return roots;
    }

    return [this.message('No session artifacts yet. Run a scan or REPL query.')];
  }

  private message(text: string): EfvibeSessionTreeItem {
    const item = new EfvibeSessionTreeItem('file', text);
    item.iconPath = new vscode.ThemeIcon('info');
    return item;
  }
}

function listDirectory(directoryPath: string | undefined): EfvibeSessionTreeItem[] {
  if (!directoryPath || !fs.existsSync(directoryPath)) {
    return [];
  }

  const entries = fs.readdirSync(directoryPath, { withFileTypes: true })
    .sort((left, right) => left.name.localeCompare(right.name));

  const items: EfvibeSessionTreeItem[] = [];

  for (const entry of entries) {
    const fullPath = path.join(directoryPath, entry.name);
    const uri = vscode.Uri.file(fullPath);

    if (entry.isDirectory()) {
      items.push(new EfvibeSessionTreeItem('folder', entry.name, uri));
      continue;
    }

    items.push(new EfvibeSessionTreeItem('file', entry.name, uri));
  }

  return items;
}

export interface EfvibeSessionExplorerRegistration {
  provider: EfvibeSessionTreeProvider;
  model: EfvibeModelTreeProvider;
}

export function registerSessionExplorer(context: vscode.ExtensionContext): EfvibeSessionExplorerRegistration {
  const provider = new EfvibeSessionTreeProvider();

  const refreshAll = (): void => {
    provider.refresh();
  };

  context.subscriptions.push(
    vscode.window.registerTreeDataProvider('efvibeSession', provider),
    vscode.commands.registerCommand('efvibe.refreshSessionExplorer', refreshAll),
    vscode.commands.registerCommand('efvibe.refreshModelTree', refreshAll),
    vscode.workspace.onDidChangeConfiguration((event) => {
      if (event.affectsConfiguration('efvibe.project')
        || event.affectsConfiguration('efvibe.context')
        || event.affectsConfiguration('efvibe.workspaceRoot')
        || event.affectsConfiguration('efvibe.startupProject')
        || event.affectsConfiguration('efvibe.toolPath')) {
        refreshAll();
      }
    }),
  );

  return { provider, model: provider.model };
}
