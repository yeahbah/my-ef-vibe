import * as vscode from 'vscode';
import type { AboutJsonPayload } from './cliRunner';
import { getSearchDirectory, readSettings } from './config';
import { runAboutJson } from './cliRunner';
import { getDbContextSessionDirectory } from './sessionPaths';

export class EfvibeStatusBar {
  private readonly item = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Left, 100);

  private lastAbout: AboutJsonPayload | undefined;

  constructor(context: vscode.ExtensionContext) {
    this.item.command = 'efvibe.startRepl';
    this.item.tooltip = 'Start efvibe REPL';
    context.subscriptions.push(this.item);
  }

  showConfigured(): void {
    const settings = readSettings();
    const contextName = settings.context || 'efvibe';
    this.item.text = `$(database) ${contextName}`;
    this.item.tooltip = this.buildTooltip(settings, undefined);
    this.item.show();
  }

  async refresh(): Promise<void> {
    const folder = vscode.workspace.workspaceFolders?.[0];
    const settings = readSettings(folder);
    const searchDirectory = getSearchDirectory(settings, folder);
    const cwd = folder?.uri.fsPath ?? searchDirectory;
    const contextName = settings.context || 'efvibe';

    this.item.text = '$(sync~spin) efvibe';
    this.item.show();

    const about = await runAboutJson(searchDirectory, cwd, {
      toolPath: settings.toolPath,
      dotnetFramework: settings.dotnetFramework,
    });
    this.lastAbout = about;

    this.item.text = `$(database) ${contextName}`;
    this.item.tooltip = this.buildTooltip(settings, about);
  }

  getLastAbout(): AboutJsonPayload | undefined {
    return this.lastAbout;
  }

  private buildTooltip(settings: ReturnType<typeof readSettings>, about: AboutJsonPayload | undefined): string {
    const lines: string[] = ['efvibe — click to start REPL'];

    if (about) {
      lines.push(`${about.command} ${about.toolVersion}`);
      lines.push(about.description);
    }

    if (settings.project) {
      lines.push(`EF project: ${settings.project}`);
    }

    if (settings.startupProject) {
      lines.push(`Startup: ${settings.startupProject}`);
    }

    if (settings.context) {
      lines.push(`DbContext (setting): ${settings.context}`);
    }

    if (settings.project && settings.context) {
      const sessionDir = getDbContextSessionDirectory(
        settings.workspaceRoot,
        settings.project,
        settings.context,
      );
      lines.push(`Session (expected): ${sessionDir}`);
    }

    lines.push('', 'Command: efvibe: Refresh Status');
    return lines.join('\n');
  }

  dispose(): void {
    this.item.dispose();
  }
}
