import * as fs from 'fs';
import * as path from 'path';
import type { ScanFindingDto } from './scanTypes';

export const SCAN_NOTES_FILE_NAME = 'myefvibe-scan-notes.json';

interface ScanNoteEntry {
  key: string;
  filePath: string;
  line: number;
  ruleId: string;
  note: string;
  updatedAt: string;
}

interface ScanNotesDocument {
  version: number;
  notes: ScanNoteEntry[];
}

function getNotesPath(sessionDirectory: string): string {
  return path.join(sessionDirectory, SCAN_NOTES_FILE_NAME);
}

function loadDocument(filePath: string): ScanNotesDocument {
  if (!fs.existsSync(filePath)) {
    return { version: 1, notes: [] };
  }

  try {
    const raw = fs.readFileSync(filePath, 'utf8');
    const parsed = JSON.parse(raw) as ScanNotesDocument;
    return {
      version: parsed.version ?? 1,
      notes: Array.isArray(parsed.notes) ? parsed.notes : [],
    };
  } catch {
    return { version: 1, notes: [] };
  }
}

export function loadNoteMap(sessionDirectory: string): Map<string, string> {
  const document = loadDocument(getNotesPath(sessionDirectory));
  const map = new Map<string, string>();

  for (const entry of document.notes) {
    if (entry.note?.trim()) {
      map.set(entry.key, entry.note.trim());
    }
  }

  return map;
}

export function saveFindingNote(
  sessionDirectory: string,
  finding: ScanFindingDto,
  noteKey: string,
  note: string,
): void {
  const trimmed = note.trim();

  if (!trimmed) {
    throw new Error('Note text is required.');
  }

  fs.mkdirSync(sessionDirectory, { recursive: true });

  const filePath = getNotesPath(sessionDirectory);
  const document = loadDocument(filePath);

  document.notes = document.notes.filter((entry) => entry.key !== noteKey);
  document.notes.push({
    key: noteKey,
    filePath: path.resolve(finding.filePath),
    line: finding.line,
    ruleId: finding.ruleId,
    note: trimmed,
    updatedAt: new Date().toISOString(),
  });

  fs.writeFileSync(filePath, `${JSON.stringify(document, null, 2)}\n`, 'utf8');
}
