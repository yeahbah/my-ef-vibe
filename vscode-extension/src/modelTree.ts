import * as vscode from 'vscode';
import type { TablesJsonEntry, TablesJsonPayload } from './cliRunner';
import { runTablesJson } from './cliRunner';
import { getSearchDirectory, getWorkspaceFolder, readSettings } from './config';
type ModelTreeNodeKind = 'context' | 'dbSet';

export class EfvibeModelTreeItem extends vscode.TreeItem {
  constructor(
    public readonly kind: ModelTreeNodeKind,
    public readonly dbSetName: string | undefined,
    public readonly entityTypeName: string | undefined,
    public readonly entityTypeFullName: string | undefined,
    label: string,
    collapsibleState: vscode.TreeItemCollapsibleState,
    description?: string,
  ) {
    super(label, collapsibleState);
    this.description = description;

    if (kind === 'dbSet' && dbSetName) {
      this.contextValue = 'dbSet';
      this.iconPath = new vscode.ThemeIcon('database');
      this.tooltip = `DbSet ${dbSetName} (${entityTypeName ?? 'entity'})`;
    } else {
      this.contextValue = 'context';
      this.iconPath = new vscode.ThemeIcon('server-environment');
    }
  }
}

export class EfvibeModelTreeProvider implements vscode.TreeDataProvider<EfvibeModelTreeItem> {
  private readonly changeEmitter = new vscode.EventEmitter<EfvibeModelTreeItem | undefined | void>();

  readonly onDidChangeTreeData = this.changeEmitter.event;

  private payload: TablesJsonPayload | undefined;

  private loadError: string | undefined;

  refresh(): void {
    this.changeEmitter.fire();
  }

  getTreeItem(element: EfvibeModelTreeItem): vscode.TreeItem {
    return element;
  }

  async getChildren(element?: EfvibeModelTreeItem): Promise<EfvibeModelTreeItem[]> {
    if (element?.kind === 'dbSet') {
      return [];
    }

    if (element?.kind === 'context') {
      return (this.payload?.tables ?? []).map((entry) => this.dbSetItem(entry));
    }

    await this.ensureLoaded();

    if (this.loadError) {
      return [this.messageItem(this.loadError)];
    }

    if (!this.payload) {
      return [this.messageItem('No DbSets found on this DbContext.')];
    }

    return [
      new EfvibeModelTreeItem(
        'context',
        undefined,
        undefined,
        undefined,
        this.payload.dbContext,
        vscode.TreeItemCollapsibleState.Expanded,
        `${this.payload.tables.length} DbSet(s)`,
      ),
    ];
  }

  private async ensureLoaded(): Promise<void> {
    const folder = getWorkspaceFolder();

    if (!folder) {
      this.payload = undefined;
      this.loadError = 'Open a workspace folder.';
      return;
    }

    const settings = readSettings(folder);

    if (!settings.project) {
      this.payload = undefined;
      this.loadError = 'Set efvibe.project in settings.';
      return;
    }

    const searchDirectory = getSearchDirectory(settings, folder);
    const tables = await runTablesJson(settings, searchDirectory, folder.uri.fsPath);

    if (!tables) {
      this.payload = undefined;
      this.loadError = 'Could not load DbSets (build CLI with --tables-json support).';
      return;
    }

    this.payload = tables;
    this.loadError = undefined;
  }

  getDbSetName(item: EfvibeModelTreeItem): string | undefined {
    return item.dbSetName;
  }

  getLastPayload(): TablesJsonPayload | undefined {
    return this.payload;
  }

  private dbSetItem(entry: TablesJsonEntry): EfvibeModelTreeItem {
    return new EfvibeModelTreeItem(
      'dbSet',
      entry.dbSet,
      entry.entityType,
      entry.entityTypeFullName,
      entry.dbSet,
      vscode.TreeItemCollapsibleState.None,
      entry.entityType,
    );
  }

  private messageItem(message: string): EfvibeModelTreeItem {
    const item = new EfvibeModelTreeItem(
      'context',
      undefined,
      undefined,
      undefined,
      message,
      vscode.TreeItemCollapsibleState.None,
    );
    item.iconPath = new vscode.ThemeIcon('info');
    return item;
  }
}
