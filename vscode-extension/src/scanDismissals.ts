import * as fs from 'fs';
import * as path from 'path';
import type { ScanFindingDto } from './scanTypes';
import { SCAN_DISMISSALS_FILE_NAME } from './sessionPaths';

interface ScanDismissalEntry {
  key: string;
  filePath: string;
  line: number;
  ruleId: string;
  note?: string;
  dismissedAt: string;
}

interface ScanDismissalsDocument {
  version: number;
  dismissals: ScanDismissalEntry[];
}

function getDismissalsPath(sessionDirectory: string): string {
  return path.join(sessionDirectory, SCAN_DISMISSALS_FILE_NAME);
}

export function loadDismissedKeys(sessionDirectory: string): Set<string> {
  const document = loadDocument(getDismissalsPath(sessionDirectory));
  return new Set(document.dismissals.map((entry) => entry.key));
}

function loadDocument(filePath: string): ScanDismissalsDocument {
  if (!fs.existsSync(filePath)) {
    return { version: 1, dismissals: [] };
  }

  try {
    const raw = fs.readFileSync(filePath, 'utf8');
    const parsed = JSON.parse(raw) as ScanDismissalsDocument;
    return {
      version: parsed.version ?? 1,
      dismissals: Array.isArray(parsed.dismissals) ? parsed.dismissals : [],
    };
  } catch {
    return { version: 1, dismissals: [] };
  }
}

export function dismissFinding(
  sessionDirectory: string,
  finding: ScanFindingDto,
  dismissalKey: string,
  note?: string,
): void {
  fs.mkdirSync(sessionDirectory, { recursive: true });

  const filePath = getDismissalsPath(sessionDirectory);
  const document = loadDocument(filePath);

  document.dismissals = document.dismissals.filter((entry) => entry.key !== dismissalKey);
  document.dismissals.push({
    key: dismissalKey,
    filePath: path.resolve(finding.filePath),
    line: finding.line,
    ruleId: finding.ruleId,
    note: note?.trim() || undefined,
    dismissedAt: new Date().toISOString(),
  });

  fs.writeFileSync(filePath, `${JSON.stringify(document, null, 2)}\n`, 'utf8');
}
