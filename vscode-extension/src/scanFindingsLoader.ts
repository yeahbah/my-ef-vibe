import * as path from 'path';
import * as vscode from 'vscode';
import { buildDismissalKey, loadScanSessionDocument, resolveFindingUri } from './scanDiagnostics';
import { loadDismissedKeys } from './scanDismissals';
import { loadNoteMap, saveFindingNote } from './scanNotes';
import type { ScanReviewItem } from './scanReviewTypes';
import type { ScanFindingDto, ScanMode, ScanSessionDocument } from './scanTypes';
import {
  discoverDeepScanFilePaths,
  getDeepScanFilePath,
  getLiteScanFilePath,
  getProjectScanDirectory,
} from './sessionPaths';

function applyNote(finding: ScanFindingDto, note: string | undefined): ScanFindingDto {
  if (!note) {
    return finding;
  }

  return { ...finding, savedNote: note };
}

function appendDocumentFindings(
  items: ScanReviewItem[],
  document: ScanSessionDocument,
  sessionDirectory: string,
  scanMode: ScanMode,
  workspaceFolders: readonly vscode.WorkspaceFolder[],
  hideDismissed: boolean,
): void {
  const dismissed = hideDismissed ? loadDismissedKeys(sessionDirectory) : new Set<string>();
  const notes = loadNoteMap(sessionDirectory);

  for (const finding of document.findings) {
    const resolvedUri = resolveFindingUri(
      finding.filePath,
      document.displayRootDirectory,
      workspaceFolders,
    );
    const resolvedPath = path.resolve(resolvedUri.fsPath);
    const key = buildDismissalKey(resolvedPath, finding.line, finding.ruleId);

    if (dismissed.has(key)) {
      continue;
    }

    const note = notes.get(key);
    const enriched = applyNote(
      { ...finding, filePath: resolvedPath },
      note ?? finding.savedNote,
    );

    items.push({
      key,
      finding: enriched,
      sessionDirectory,
      scanMode,
      displayRoot: document.displayRootDirectory,
    });
  }
}

export function loadScanReviewItems(
  workspaceRoot: string,
  projectCsprojPath: string,
  dbContextName: string,
  workspaceFolders: readonly vscode.WorkspaceFolder[],
  hideDismissed: boolean,
): ScanReviewItem[] {
  const items: ScanReviewItem[] = [];

  const litePath = getLiteScanFilePath(workspaceRoot, projectCsprojPath);
  const liteDoc = loadScanSessionDocument(litePath);

  if (liteDoc) {
    appendDocumentFindings(
      items,
      liteDoc,
      getProjectScanDirectory(workspaceRoot, projectCsprojPath),
      'lite',
      workspaceFolders,
      hideDismissed,
    );
  }

  const deepPaths = new Set<string>();

  if (dbContextName.trim()) {
    deepPaths.add(getDeepScanFilePath(workspaceRoot, projectCsprojPath, dbContextName.trim()));
  }

  for (const discovered of discoverDeepScanFilePaths(workspaceRoot, projectCsprojPath)) {
    deepPaths.add(discovered);
  }

  for (const deepPath of deepPaths) {
    const deepDoc = loadScanSessionDocument(deepPath);

    if (!deepDoc) {
      continue;
    }

    appendDocumentFindings(
      items,
      deepDoc,
      path.dirname(deepPath),
      'deep',
      workspaceFolders,
      hideDismissed,
    );
  }

  items.sort((left, right) => {
    const pathCompare = left.finding.filePath.localeCompare(right.finding.filePath);

    if (pathCompare !== 0) {
      return pathCompare;
    }

    if (left.finding.line !== right.finding.line) {
      return left.finding.line - right.finding.line;
    }

    return left.finding.ruleId.localeCompare(right.finding.ruleId);
  });

  return items;
}

export { saveFindingNote };
