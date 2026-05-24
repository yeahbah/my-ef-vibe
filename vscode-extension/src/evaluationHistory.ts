import * as vscode from 'vscode';
import type { EvaluationJsonPayload } from './evaluationTypes';

export interface EvaluationHistoryEntry {
  expression: string;
  totalMs: number;
  databaseMs?: number;
  rowCount?: number;
  sqlCommandCount: number;
  resultKind: string;
  timestamp: string;
}

const HISTORY_KEY = 'efvibe.evaluationHistory';
const BASELINE_KEY = 'efvibe.compareBaseline';
const MAX_ENTRIES = 50;

export class EvaluationHistoryStore {
  constructor(private readonly globalState: Pick<vscode.Memento, 'get' | 'update'>) {}

  async record(expression: string, payload: EvaluationJsonPayload): Promise<void> {
    const entry: EvaluationHistoryEntry = {
      expression,
      totalMs: payload.metrics.totalMs,
      databaseMs: payload.metrics.databaseMs,
      rowCount: payload.metrics.rowCount,
      sqlCommandCount: payload.metrics.sqlCommandCount,
      resultKind: payload.metrics.resultKind,
      timestamp: new Date().toISOString(),
    };

    const history = this.getHistory();
    history.unshift(entry);

    if (history.length > MAX_ENTRIES) {
      history.length = MAX_ENTRIES;
    }

    await this.globalState.update(HISTORY_KEY, history);
  }

  getHistory(): EvaluationHistoryEntry[] {
    return this.globalState.get<EvaluationHistoryEntry[]>(HISTORY_KEY, []);
  }

  async setCompareBaseline(): Promise<EvaluationHistoryEntry | undefined> {
    const latest = this.getHistory()[0];

    if (!latest) {
      return undefined;
    }

    await this.globalState.update(BASELINE_KEY, latest);
    return latest;
  }

  getCompareBaseline(): EvaluationHistoryEntry | undefined {
    return this.globalState.get<EvaluationHistoryEntry | undefined>(BASELINE_KEY);
  }

  async clearCompareBaseline(): Promise<void> {
    await this.globalState.update(BASELINE_KEY, undefined);
  }
}
