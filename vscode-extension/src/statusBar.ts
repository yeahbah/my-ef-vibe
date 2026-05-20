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

    if (!settings.project) {
      this.showConfigured();
      return;
    }

    const searchDirectory = getSearchDirectory(settings, folder);
    const cwd = folder?.uri.fsPath ?? searchDirectory;

    this.item.text = '$(sync~spin) efvibe';
    this.item.show();

    const about = await runAboutJson(settings, searchDirectory, cwd);
    this.lastAbout = about;

    if (about) {
      const connection = about.connectionState ? ` · ${about.connectionState}` : '';
      this.item.text = `$(database) ${about.dbContext}${connection}`;
      this.item.tooltip = this.buildTooltip(settings, about);
    } else {
      const contextName = settings.context || 'efvibe';
      this.item.text = `$(database) ${contextName}`;
      this.item.tooltip = this.buildTooltip(settings, undefined);
    }
  }

  getLastAbout(): AboutJsonPayload | undefined {
    return this.lastAbout;
  }

  private buildTooltip(settings: ReturnType<typeof readSettings>, about: AboutJsonPayload | undefined): string {
    const lines: string[] = ['efvibe — click to start REPL'];

    if (settings.project) {
      lines.push(`EF project: ${settings.project}`);
    }

    if (settings.startupProject) {
      lines.push(`Startup: ${settings.startupProject}`);
    }

    if (settings.context) {
      lines.push(`Context (setting): ${settings.context}`);
    }

    if (about) {
      lines.push(`DbContext: ${about.dbContextFullName ?? about.dbContext}`);
      if (about.providerName) {
        lines.push(`Provider: ${about.providerName}`);
      }
      if (about.connectionState) {
        lines.push(`Connection: ${about.connectionState}`);
      }
      if (about.sessionDirectory) {
        lines.push(`Session: ${about.sessionDirectory}`);
      }
    } else if (settings.project && settings.context) {
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
