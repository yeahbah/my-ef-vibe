import type { ScanFindingDto, ScanMode } from './scanTypes';

export interface ScanReviewItem {
  key: string;
  finding: ScanFindingDto;
  sessionDirectory: string;
  scanMode: ScanMode;
  displayRoot: string;
}
