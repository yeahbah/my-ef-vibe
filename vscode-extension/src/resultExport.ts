import * as vscode from 'vscode';
import type { EvaluationJsonPayload } from './evaluationTypes';

export type ExportFormat = 'csv' | 'json';

export function canExportPayload(payload: EvaluationJsonPayload): boolean {
  if (!payload.success) {
    return false;
  }

  if (payload.rows && payload.rows.length > 0) {
    return true;
  }

  return payload.value !== undefined && payload.value !== null;
}

export function buildExportContent(payload: EvaluationJsonPayload, format: ExportFormat): string | undefined {
  if (!canExportPayload(payload)) {
    return undefined;
  }

  const rows = payload.rows && payload.rows.length > 0
    ? payload.rows
    : [{ value: String(payload.value ?? '') }];

  return format === 'json' ? buildJson(rows) : buildCsv(rows);
}

export async function exportEvaluationPayload(
  payload: EvaluationJsonPayload,
  format: ExportFormat,
  defaultDirectory?: string,
): Promise<void> {
  const content = buildExportContent(payload, format);

  if (!content) {
    vscode.window.showWarningMessage('Nothing to export from the last result.');
    return;
  }

  const timestamp = formatExportTimestamp(new Date());
  const defaultUri = vscode.Uri.file(
    defaultDirectory
      ? `${defaultDirectory}/myefvibe-export-${timestamp}.${format}`
      : `myefvibe-export-${timestamp}.${format}`,
  );

  const target = await vscode.window.showSaveDialog({
    defaultUri,
    filters: format === 'csv'
      ? { 'CSV': ['csv'] }
      : { 'JSON': ['json'] },
    saveLabel: 'Export',
  });

  if (!target) {
    return;
  }

  await vscode.workspace.fs.writeFile(target, Buffer.from(content, 'utf8'));

  const rowCount = payload.rows?.length ?? 1;
  vscode.window.showInformationMessage(
    `efvibe: exported ${rowCount} row(s) to ${target.fsPath}`,
  );
}

function buildCsv(rows: Array<Record<string, string>>): string {
  const columns = collectColumns(rows);
  const lines = [
    columns.map(escapeCsv).join(','),
    ...rows.map((row) => columns.map((column) => escapeCsv(row[column] ?? '')).join(',')),
  ];

  return `${lines.join('\n')}\n`;
}

function buildJson(rows: Array<Record<string, string>>): string {
  return `${JSON.stringify(rows, null, 2)}\n`;
}

function collectColumns(rows: Array<Record<string, string>>): string[] {
  const columns = new Set<string>();

  for (const row of rows) {
    for (const key of Object.keys(row)) {
      columns.add(key);
    }
  }

  return [...columns].sort();
}

function escapeCsv(value: string): string {
  if (value.length === 0) {
    return value;
  }

  if (/[",\n\r]/.test(value)) {
    return `"${value.replace(/"/g, '""')}"`;
  }

  return value;
}

function formatExportTimestamp(date: Date): string {
  const pad = (part: number) => String(part).padStart(2, '0');
  return `${date.getFullYear()}${pad(date.getMonth() + 1)}${pad(date.getDate())}-${pad(date.getHours())}${pad(date.getMinutes())}${pad(date.getSeconds())}`;
}
