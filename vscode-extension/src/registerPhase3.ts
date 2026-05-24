import * as vscode from 'vscode';
import { registerEfvibeCodeLens } from './codeLensProvider';
import { EfvibeChartsPanel } from './chartsPanel';
import { runDbInfoJson } from './cliRunner';
import { EfvibeDbInfoPanel } from './dbInfoPanel';
import { EvaluationHistoryStore } from './evaluationHistory';
import { registerEfvibeCompletion, invalidateCompletionCache } from './efvibeCompletion';
import { pickEntityCommand } from './entityPicker';
import { EfvibeQueryPlanPanel } from './queryPlanPanel';
import { EfvibeResultPanel } from './resultPanel';
import { getScanRuleDocsUrl } from './scanRuleDocs';
import type { EfvibeSettings } from './config';

export interface Phase3Context {
  evaluationHistory: EvaluationHistoryStore;
  requireWorkspace: () => Promise<{
    folder: vscode.WorkspaceFolder;
    settings: EfvibeSettings;
    searchDirectory: string;
  } | undefined>;
  runFromResultPanel: (
    request: { expression: string; withPlan: boolean },
    context: {
      folder: vscode.WorkspaceFolder;
      settings: EfvibeSettings;
      searchDirectory: string;
    },
  ) => Promise<void>;
  resolveExportDirectory: (
    settings: EfvibeSettings,
    folder: vscode.WorkspaceFolder,
  ) => string;
  extensionContext: vscode.ExtensionContext;
}

export function registerPhase3Features(context: vscode.ExtensionContext, phase3: Phase3Context): void {
  registerEfvibeCompletion(context);
  registerEfvibeCodeLens(context);

  context.subscriptions.push(
    vscode.commands.registerCommand('efvibe.pickEntity', () => pickEntityCommand()),
    vscode.commands.registerCommand('efvibe.showDbInfo', () => showDbInfoCommand(phase3)),
    vscode.commands.registerCommand('efvibe.showQueryPlan', () => showQueryPlanCommand()),
    vscode.commands.registerCommand('efvibe.showCharts', () => showChartsCommand(phase3)),
    vscode.commands.registerCommand('efvibe.setCompareBaseline', () => setCompareBaselineCommand(phase3)),
    vscode.commands.registerCommand('efvibe.clearCompareBaseline', () => clearCompareBaselineCommand(phase3)),
    vscode.workspace.onDidChangeConfiguration((event) => {
      if (event.affectsConfiguration('efvibe.project')
        || event.affectsConfiguration('efvibe.context')
        || event.affectsConfiguration('efvibe.toolPath')) {
        invalidateCompletionCache();
      }
    }),
  );
}

export function recordEvaluationHistory(
  store: EvaluationHistoryStore,
  expression: string,
  payload: import('./evaluationTypes').EvaluationJsonPayload,
): void {
  if (payload.success) {
    void store.record(expression, payload);
  }
}

async function showDbInfoCommand(phase3: Phase3Context): Promise<void> {
  const context = await phase3.requireWorkspace();

  if (!context) {
    return;
  }

  await vscode.window.withProgress(
    { location: vscode.ProgressLocation.Notification, title: 'efvibe :dbinfo' },
    async () => {
      const payload = await runDbInfoJson(
        context.settings,
        context.searchDirectory,
        context.folder.uri.fsPath,
      );

      if (!payload) {
        void vscode.window.showErrorMessage('efvibe: Could not load dbinfo (build CLI with --dbinfo-json).');
        return;
      }

      EfvibeDbInfoPanel.show(payload);
    },
  );
}

function showQueryPlanCommand(): void {
  const payload = EfvibeResultPanel.getLastPayload();

  if (!payload) {
    void vscode.window.showInformationMessage('Run an expression with **Run Plan** first, or open a deep-scan finding with a plan.');
    return;
  }

  if (payload.queryPlan?.trim()) {
    const sql = payload.sql[0] ?? payload.translatedSql;
    EfvibeQueryPlanPanel.show('Query plan', payload.queryPlan, sql);
    return;
  }

  if (payload.queryPlanNote?.trim()) {
    EfvibeQueryPlanPanel.show('Query plan', payload.queryPlanNote);
    return;
  }

  void vscode.window.showInformationMessage('No query plan in the last result. Use **Run Plan** in the result panel.');
}

function showChartsCommand(phase3: Phase3Context): void {
  const history = phase3.evaluationHistory.getHistory();
  const baseline = phase3.evaluationHistory.getCompareBaseline();
  const latest = history[0];

  EfvibeChartsPanel.show(history, baseline, latest);
}

async function setCompareBaselineCommand(phase3: Phase3Context): Promise<void> {
  const baseline = await phase3.evaluationHistory.setCompareBaseline();

  if (!baseline) {
    void vscode.window.showInformationMessage('Run a query first, then set a compare baseline.');
    return;
  }

  void vscode.window.showInformationMessage(`efvibe: compare baseline set (${baseline.totalMs} ms).`);
}

async function clearCompareBaselineCommand(phase3: Phase3Context): Promise<void> {
  await phase3.evaluationHistory.clearCompareBaseline();
  void vscode.window.showInformationMessage('efvibe: compare baseline cleared.');
}

export function openScanRuleDocs(ruleId: string | undefined): void {
  const url = getScanRuleDocsUrl(ruleId);

  if (url) {
    void vscode.env.openExternal(vscode.Uri.parse(url));
  }
}
