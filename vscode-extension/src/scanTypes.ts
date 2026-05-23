export type ScanMode = 'lite' | 'deep';

export interface ScanFindingDto {
  filePath: string;
  line: number;
  code: string;
  ruleId: string;
  message: string;
  severity?: string;
  recommendation?: string;
  translatedSql?: string;
  sqlTranslationNote?: string;
  queryPlan?: string;
  queryPlanNote?: string;
  savedNote?: string;
}

export interface ScanSessionDocument {
  version: number;
  scanMode: string;
  scannedAt: string;
  filesScanned: number;
  projectsScanned: number;
  displayRootDirectory: string;
  findings: ScanFindingDto[];
}

export interface ScanCiOutputDocument {
  scanMode: string;
  savedPath: string;
  totalFindings: number;
  ciFailed: boolean;
  filesScanned: number;
  projectsScanned: number;
  findings: ScanFindingDto[];
}
