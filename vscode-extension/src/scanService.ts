import * as path from 'path';
import * as vscode from 'vscode';
import { getSearchDirectory, getWorkspaceFolder, readSettings } from './config';
import { dismissFinding } from './scanDismissals';
import {
  applyDiagnosticsFromReviewItems,
  createScanDiagnosticCollection,
  rangeForLine,
} from './scanDiagnostics';
import {
  discoverDeepScanFilePaths,
  getDeepScanFilePath,
  getLiteScanFilePath,
} from './sessionPaths';
import { loadScanReviewItems, saveFindingNote } from './scanFindingsLoader';
import { EfvibeScanReviewPanel } from './scanReviewPanel';
import { runScan } from './scanRunner';
import type { ScanReviewItem } from './scanReviewTypes';
import type { ScanMode } from './scanTypes';

export type EfvibeScanModeSetting = 'lite' | 'deep';

export interface EfvibeScanSettings {
  mode: EfvibeScanModeSetting;
  respectDismissals: boolean;
  refreshOnSave: boolean;
  onSave: boolean;
  minSeverity: string;
  problemsPanel: boolean;
  openReviewOnScan: boolean;
}

export function readScanSettings(workspaceFolder?: vscode.WorkspaceFolder): EfvibeScanSettings {
  const config = vscode.workspace.getConfiguration('efvibe', workspaceFolder?.uri);
  const modeRaw = config.get<string>('scan.mode', 'lite').trim().toLowerCase();

  return {
    mode: modeRaw === 'deep' ? 'deep' : 'lite',
    respectDismissals: config.get<boolean>('scan.respectDismissals', true),
    refreshOnSave: config.get<boolean>('scan.refreshOnSave', true),
    onSave: config.get<boolean>('scan.onSave', false),
    minSeverity: config.get<string>('scan.minSeverity', '').trim(),
    problemsPanel: config.get<boolean>('scan.problemsPanel', false),
    openReviewOnScan: config.get<boolean>('scan.openReviewOnScan', true),
  };
}

export class EfvibeScanService implements vscode.Disposable {
  private readonly collection = createScanDiagnosticCollection();

  private reviewItems: ScanReviewItem[] = [];

  private readonly disposables: vscode.Disposable[] = [];

  private watchers: vscode.FileSystemWatcher[] = [];

  private onSaveScanTimer: ReturnType<typeof setTimeout> | undefined;

  private onSaveScanRunning = false;

  private onSaveScanPending = false;

  constructor(private readonly extensionContext: vscode.ExtensionContext) {
    this.disposables.push(this.collection);

    this.disposables.push(
      vscode.commands.registerCommand('efvibe.scanWorkspace', () => this.scanWorkspaceCommand()),
      vscode.commands.registerCommand('efvibe.scanDeep', () => this.scanWorkspaceCommand('deep')),
      vscode.commands.registerCommand('efvibe.openScanReview', () => this.openScanReviewCommand()),
      vscode.commands.registerCommand('efvibe.refreshScanDiagnostics', () => this.refreshFromArtifacts()),
      vscode.commands.registerCommand('efvibe.dismissScanFinding', () => this.dismissAtCursorCommand()),
    );

    void this.refreshFromArtifacts();
    this.setupWatchers();

    this.disposables.push(
      vscode.workspace.onDidChangeConfiguration((event) => {
        if (event.affectsConfiguration('efvibe.scan') || event.affectsConfiguration('efvibe.project')
          || event.affectsConfiguration('efvibe.context')
          || event.affectsConfiguration('efvibe.workspaceRoot')) {
          this.setupWatchers();
          void this.refreshFromArtifacts();
        }
      }),
      vscode.workspace.onDidSaveTextDocument((document) => this.onDocumentSaved(document)),
    );
  }

  dispose(): void {
    if (this.onSaveScanTimer) {
      clearTimeout(this.onSaveScanTimer);
      this.onSaveScanTimer = undefined;
    }

    for (const watcher of this.watchers) {
      watcher.dispose();
    }

    this.watchers = [];

    for (const disposable of this.disposables) {
      disposable.dispose();
    }

    this.collection.dispose();
  }

  private getWorkspaceFolders(): readonly vscode.WorkspaceFolder[] {
    return vscode.workspace.workspaceFolders ?? [];
  }

  private getProjectSettings(): { settings: ReturnType<typeof readSettings>; folder: vscode.WorkspaceFolder } | undefined {
    const folder = getWorkspaceFolder();

    if (!folder) {
      return undefined;
    }

    const settings = readSettings(folder);

    if (!settings.project) {
      return undefined;
    }

    return { settings, folder };
  }

  private resolveArtifactPaths(): { lite?: string; deepPaths: string[] } {
    const project = this.getProjectSettings();

    if (!project) {
      return { deepPaths: [] };
    }

    const { settings } = project;
    const lite = getLiteScanFilePath(settings.workspaceRoot, settings.project);
    const deepPaths = new Set(discoverDeepScanFilePaths(settings.workspaceRoot, settings.project));

    if (settings.context.trim()) {
      deepPaths.add(
        getDeepScanFilePath(settings.workspaceRoot, settings.project, settings.context.trim()),
      );
    }

    return { lite, deepPaths: [...deepPaths].sort((left, right) => left.localeCompare(right)) };
  }

  private loadReviewItems(): ScanReviewItem[] {
    const project = this.getProjectSettings();

    if (!project) {
      return [];
    }

    const scanSettings = readScanSettings(project.folder);

    return loadScanReviewItems(
      project.settings.workspaceRoot,
      project.settings.project,
      project.settings.context,
      this.getWorkspaceFolders(),
      scanSettings.respectDismissals,
    );
  }

  private setupWatchers(): void {
    for (const watcher of this.watchers) {
      watcher.dispose();
    }

    this.watchers = [];

    const scanSettings = readScanSettings();

    if (!scanSettings.refreshOnSave) {
      return;
    }

    const { lite, deepPaths } = this.resolveArtifactPaths();
    const patterns = [lite, ...deepPaths].filter((entry): entry is string => Boolean(entry));

    for (const pattern of patterns) {
      const watcher = vscode.workspace.createFileSystemWatcher(pattern);
      watcher.onDidChange(() => void this.refreshFromArtifacts());
      watcher.onDidCreate(() => void this.refreshFromArtifacts());
      this.watchers.push(watcher);
    }
  }

  async refreshFromArtifacts(): Promise<ScanReviewItem[]> {
    this.reviewItems = this.loadReviewItems();
    const scanSettings = readScanSettings();

    this.collection.clear();

    if (scanSettings.problemsPanel) {
      applyDiagnosticsFromReviewItems(
        this.collection,
        this.reviewItems,
        this.getWorkspaceFolders(),
      );
    }

    return this.reviewItems;
  }

  private showReviewPanel(startIndex = 0): void {
    EfvibeScanReviewPanel.show(
      this.extensionContext,
      this.reviewItems,
      startIndex,
      {
        onDismiss: (item, dismissalNote) => this.dismissReviewItem(item, dismissalNote),
        onSaveNote: (item, note) => this.saveReviewNote(item, note),
        onGoToSource: (item) => this.goToReviewSource(item),
      },
    );
  }

  private async openScanReviewCommand(): Promise<void> {
    const project = this.getProjectSettings();

    if (!project) {
      void vscode.window.showWarningMessage('efvibe: Set efvibe.project before opening scan review.');
      return;
    }

    await this.refreshFromArtifacts();
    this.showReviewPanel(0);
  }

  private onDocumentSaved(document: vscode.TextDocument): void {
    const scanSettings = readScanSettings();

    if (!scanSettings.onSave) {
      return;
    }

    if (document.languageId !== 'csharp' || document.uri.scheme !== 'file') {
      return;
    }

    if (!vscode.workspace.getWorkspaceFolder(document.uri)) {
      return;
    }

    if (!this.getProjectSettings()) {
      return;
    }

    if (this.onSaveScanTimer) {
      clearTimeout(this.onSaveScanTimer);
    }

    this.onSaveScanTimer = setTimeout(() => {
      this.onSaveScanTimer = undefined;
      void this.runOnSaveScan();
    }, 2000);
  }

  private async runOnSaveScan(): Promise<void> {
    if (this.onSaveScanRunning) {
      this.onSaveScanPending = true;
      return;
    }

    this.onSaveScanRunning = true;

    try {
      await this.scanWorkspaceInternal({
        silent: true,
        openReview: false,
      });
    } finally {
      this.onSaveScanRunning = false;

      if (this.onSaveScanPending) {
        this.onSaveScanPending = false;
        void this.runOnSaveScan();
      }
    }
  }

  async scanWorkspaceCommand(modeOverride?: ScanMode): Promise<void> {
    await this.scanWorkspaceInternal({ modeOverride, silent: false });
  }

  private async scanWorkspaceInternal(options?: {
    modeOverride?: ScanMode;
    silent?: boolean;
    openReview?: boolean;
  }): Promise<void> {
    const folder = getWorkspaceFolder();

    if (!folder) {
      if (!options?.silent) {
        void vscode.window.showWarningMessage('efvibe: Open a workspace folder before scanning.');
      }

      return;
    }

    const settings = readSettings(folder);

    if (!settings.project) {
      if (!options?.silent) {
        void vscode.window.showWarningMessage('efvibe: Set efvibe.project to your EF Core .csproj path.');
      }

      return;
    }

    const scanSettings = readScanSettings(folder);
    const mode = options?.modeOverride ?? scanSettings.mode;

    const openReview = options?.openReview ?? scanSettings.openReviewOnScan;
    const silent = options?.silent ?? false;

    const runScanJob = async (): Promise<void> => {
      const searchDirectory = getSearchDirectory(settings, folder);
      const result = await runScan(settings, searchDirectory, folder.uri.fsPath, {
        mode,
        respectDismissals: scanSettings.respectDismissals,
        minSeverity: scanSettings.minSeverity || undefined,
      });

      const items = await this.refreshFromArtifacts();
      const count = result.output?.totalFindings ?? items.length;
      const reportedMode = result.output?.scanMode?.trim().toLowerCase();

      if (reportedMode && reportedMode !== mode) {
        const detail = `CLI reported scan mode "${reportedMode}" but "${mode}" was requested.`;
        if (silent) {
          void vscode.window.setStatusBarMessage(`efvibe: ${detail}`, 5000);
        } else {
          void vscode.window.showWarningMessage(`efvibe: ${detail}`);
        }
      }

      if (result.exitCode !== 0 && !result.output) {
        if (silent) {
          void vscode.window.setStatusBarMessage(
            `efvibe scan ${mode} failed (exit ${result.exitCode})`,
            4000,
          );
        } else {
          const detail = (result.stderr || result.stdout).trim().slice(0, 500);
          void vscode.window.showErrorMessage(
            `efvibe scan ${mode} failed (exit ${result.exitCode}).${detail ? ` ${detail}` : ''}`,
          );
        }

        return;
      }

      if (openReview) {
        const startIndex = Math.max(0, items.findIndex((item) => item.scanMode === mode));
        this.showReviewPanel(startIndex);
      } else if (silent) {
        void vscode.window.setStatusBarMessage(`efvibe scan ${mode}: ${count} finding(s)`, 3000);
      } else {
        void vscode.window.showInformationMessage(`efvibe scan ${mode}: ${count} finding(s).`);
      }
    };

    if (silent) {
      await runScanJob();
      return;
    }

    await vscode.window.withProgress(
      {
        location: vscode.ProgressLocation.Notification,
        title: `efvibe scan ${mode}`,
        cancellable: false,
      },
      runScanJob,
    );
  }

  private async dismissReviewItem(item: ScanReviewItem, dismissalNote?: string): Promise<void> {
    dismissFinding(item.sessionDirectory, item.finding, item.key, dismissalNote);

    const currentIndex = this.reviewItems.findIndex((entry) => entry.key === item.key);
    this.reviewItems = this.reviewItems.filter((entry) => entry.key !== item.key);

    if (readScanSettings().problemsPanel) {
      await this.refreshFromArtifacts();
    } else {
      this.collection.clear();
    }

    if (this.reviewItems.length === 0) {
      this.showReviewPanel(0);
      void vscode.window.showInformationMessage('efvibe: All findings dismissed.');
      return;
    }

    const nextIndex = Math.min(currentIndex, this.reviewItems.length - 1);
    this.showReviewPanel(Math.max(0, nextIndex));
  }

  private async saveReviewNote(item: ScanReviewItem, note: string): Promise<void> {
    const trimmed = note.trim();

    if (!trimmed) {
      void vscode.window.showWarningMessage('efvibe: Note text is required.');
      return;
    }

    try {
      saveFindingNote(item.sessionDirectory, item.finding, item.key, trimmed);
    } catch (error) {
      void vscode.window.showErrorMessage(
        `efvibe: ${error instanceof Error ? error.message : 'Could not save note.'}`,
      );
      return;
    }

    const index = this.reviewItems.findIndex((entry) => entry.key === item.key);

    if (index >= 0) {
      this.reviewItems[index] = {
        ...item,
        finding: { ...item.finding, savedNote: trimmed },
      };
    }

    this.showReviewPanel(index >= 0 ? index : 0);
    void vscode.window.showInformationMessage('efvibe: Note saved.');
  }

  private async goToReviewSource(item: ScanReviewItem): Promise<void> {
    const uri = vscode.Uri.file(item.finding.filePath);
    const lineIndex = Math.max(0, item.finding.line - 1);

    try {
      const document = await vscode.workspace.openTextDocument(uri);
      const editor = await vscode.window.showTextDocument(document, {
        viewColumn: vscode.ViewColumn.One,
        preserveFocus: false,
      });
      const range = rangeForLine(uri, lineIndex);
      editor.selection = new vscode.Selection(range.start, range.start);
      editor.revealRange(range, vscode.TextEditorRevealType.InCenter);
    } catch {
      void vscode.window.showErrorMessage(`efvibe: Could not open ${item.finding.filePath}`);
    }
  }

  private async dismissAtCursorCommand(): Promise<void> {
    await this.refreshFromArtifacts();

    const editor = vscode.window.activeTextEditor;

    if (!editor || editor.document.languageId !== 'csharp') {
      void vscode.window.showWarningMessage('efvibe: Open a C# file and place the cursor on a finding.');
      return;
    }

    const filePath = path.resolve(editor.document.uri.fsPath);
    const line = editor.selection.active.line + 1;
    const index = this.reviewItems.findIndex(
      (finding) => path.resolve(finding.finding.filePath) === filePath && finding.finding.line === line,
    );

    if (index < 0) {
      void vscode.window.showWarningMessage('efvibe: No scan finding at the cursor line. Open Scan Review instead.');
      return;
    }

    this.showReviewPanel(index);
  }
}

export function registerScanService(context: vscode.ExtensionContext): EfvibeScanService {
  const service = new EfvibeScanService(context);
  context.subscriptions.push(service);
  return service;
}
