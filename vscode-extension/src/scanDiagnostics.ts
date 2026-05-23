import * as fs from 'fs';
import * as path from 'path';
import * as vscode from 'vscode';
import type { ScanReviewItem } from './scanReviewTypes';
import type { ScanFindingDto, ScanSessionDocument } from './scanTypes';

const DIAGNOSTIC_SOURCE = 'efvibe';

const RULE_SEVERITY: Record<string, vscode.DiagnosticSeverity> = {
  'unbounded-materialize': vscode.DiagnosticSeverity.Error,
  'n-plus-one': vscode.DiagnosticSeverity.Error,
  'raw-sql': vscode.DiagnosticSeverity.Warning,
  'raw-sql-unparameterized': vscode.DiagnosticSeverity.Error,
  cartesian: vscode.DiagnosticSeverity.Warning,
  'client-eval': vscode.DiagnosticSeverity.Warning,
  'unordered-take': vscode.DiagnosticSeverity.Warning,
  'first-without-take': vscode.DiagnosticSeverity.Warning,
  'query-site': vscode.DiagnosticSeverity.Information,
  'unmapped-entity': vscode.DiagnosticSeverity.Warning,
  'invalid-navigation-include': vscode.DiagnosticSeverity.Warning,
};

function parseSeverity(raw: string | undefined, ruleId: string): vscode.DiagnosticSeverity {
  switch (raw?.trim().toLowerCase()) {
    case 'info':
      return vscode.DiagnosticSeverity.Information;
    case 'warning':
      return vscode.DiagnosticSeverity.Warning;
    case 'error':
      return vscode.DiagnosticSeverity.Error;
    case 'critical':
      return vscode.DiagnosticSeverity.Error;
    default:
      return RULE_SEVERITY[ruleId] ?? vscode.DiagnosticSeverity.Warning;
  }
}

export function resolveFindingUri(
  filePath: string,
  displayRoot: string,
  workspaceFolders: readonly vscode.WorkspaceFolder[],
): vscode.Uri {
  if (path.isAbsolute(filePath)) {
    return vscode.Uri.file(filePath);
  }

  const fromDisplay = path.join(displayRoot, filePath);
  if (fs.existsSync(fromDisplay)) {
    return vscode.Uri.file(fromDisplay);
  }

  for (const folder of workspaceFolders) {
    const candidate = path.join(folder.uri.fsPath, filePath);
    if (fs.existsSync(candidate)) {
      return vscode.Uri.file(candidate);
    }
  }

  return vscode.Uri.file(fromDisplay);
}

export function buildDismissalKey(filePath: string, line: number, ruleId: string): string {
  return `${path.resolve(filePath)}|${line}|${ruleId}`;
}

/** Full-line range without MAX_SAFE_INTEGER (breaks Roslyn LSP code actions). */
export function rangeForLine(uri: vscode.Uri, lineIndex: number): vscode.Range {
  const openDocument = vscode.workspace.textDocuments.find(
    (document) => document.uri.toString() === uri.toString(),
  );

  if (openDocument && lineIndex >= 0 && lineIndex < openDocument.lineCount) {
    return openDocument.lineAt(lineIndex).range;
  }

  if (fs.existsSync(uri.fsPath)) {
    const lines = fs.readFileSync(uri.fsPath, 'utf8').split(/\r?\n/u);

    if (lineIndex >= 0 && lineIndex < lines.length) {
      return new vscode.Range(lineIndex, 0, lineIndex, lines[lineIndex].length);
    }
  }

  return new vscode.Range(lineIndex, 0, lineIndex, 0);
}

export function findingToDiagnostic(
  finding: ScanFindingDto,
  displayRoot: string,
  workspaceFolders: readonly vscode.WorkspaceFolder[],
): { uri: vscode.Uri; diagnostic: vscode.Diagnostic } {
  const uri = resolveFindingUri(finding.filePath, displayRoot, workspaceFolders);
  const lineIndex = Math.max(0, finding.line - 1);
  const range = rangeForLine(uri, lineIndex);

  const diagnostic = new vscode.Diagnostic(
    range,
    finding.message,
    parseSeverity(finding.severity, finding.ruleId),
  );
  diagnostic.source = DIAGNOSTIC_SOURCE;
  diagnostic.code = finding.ruleId;

  const related: vscode.DiagnosticRelatedInformation[] = [];

  if (finding.recommendation?.trim()) {
    related.push(
      new vscode.DiagnosticRelatedInformation(
        new vscode.Location(uri, range),
        finding.recommendation.trim(),
      ),
    );
  }

  if (finding.translatedSql?.trim()) {
    related.push(
      new vscode.DiagnosticRelatedInformation(
        new vscode.Location(uri, range),
        `SQL: ${finding.translatedSql.trim()}`,
      ),
    );
  }

  if (finding.sqlTranslationNote?.trim()) {
    related.push(
      new vscode.DiagnosticRelatedInformation(
        new vscode.Location(uri, range),
        finding.sqlTranslationNote.trim(),
      ),
    );
  }

  if (finding.savedNote?.trim()) {
    related.push(
      new vscode.DiagnosticRelatedInformation(
        new vscode.Location(uri, range),
        `Note: ${finding.savedNote.trim()}`,
      ),
    );
  }

  if (related.length > 0) {
    diagnostic.relatedInformation = related;
  }

  return { uri, diagnostic };
}

export function loadScanSessionDocument(filePath: string): ScanSessionDocument | undefined {
  if (!fs.existsSync(filePath)) {
    return undefined;
  }

  try {
    const raw = fs.readFileSync(filePath, 'utf8');
    return JSON.parse(raw) as ScanSessionDocument;
  } catch {
    return undefined;
  }
}

export function indexFindingsFromDocument(
  document: ScanSessionDocument,
  workspaceFolders: readonly vscode.WorkspaceFolder[],
  grouped: Map<string, vscode.Diagnostic[]>,
  byKey: Map<string, ScanFindingDto>,
): void {
  for (const finding of document.findings) {
    const { uri, diagnostic } = findingToDiagnostic(
      finding,
      document.displayRootDirectory,
      workspaceFolders,
    );
    const uriKey = uri.toString();
    const list = grouped.get(uriKey) ?? [];
    list.push(diagnostic);
    grouped.set(uriKey, list);

    const resolvedPath = path.resolve(
      resolveFindingUri(finding.filePath, document.displayRootDirectory, workspaceFolders).fsPath,
    );
    byKey.set(buildDismissalKey(resolvedPath, finding.line, finding.ruleId), {
      ...finding,
      filePath: resolvedPath,
    });
  }
}

export function applyGroupedDiagnostics(
  collection: vscode.DiagnosticCollection,
  grouped: Map<string, vscode.Diagnostic[]>,
): void {
  for (const [uriKey, diagnostics] of grouped) {
    collection.set(vscode.Uri.parse(uriKey), diagnostics);
  }
}

export function applyDiagnosticsFromReviewItems(
  collection: vscode.DiagnosticCollection,
  items: ScanReviewItem[],
  workspaceFolders: readonly vscode.WorkspaceFolder[],
): void {
  const grouped = new Map<string, vscode.Diagnostic[]>();
  const byKey = new Map<string, ScanFindingDto>();

  for (const item of items) {
    indexFindingsFromDocument(
      {
        version: 4,
        scanMode: item.scanMode,
        scannedAt: '',
        filesScanned: 0,
        projectsScanned: 0,
        displayRootDirectory: item.displayRoot,
        findings: [item.finding],
      },
      workspaceFolders,
      grouped,
      byKey,
    );
  }

  applyGroupedDiagnostics(collection, grouped);
}

export function createScanDiagnosticCollection(): vscode.DiagnosticCollection {
  return vscode.languages.createDiagnosticCollection(DIAGNOSTIC_SOURCE);
}
