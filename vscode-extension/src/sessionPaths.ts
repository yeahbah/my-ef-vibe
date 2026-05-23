import * as os from 'os';
import * as path from 'path';

const INVALID_CHARS = /[<>:"/\\|?*\u0000-\u001f]/g;

export function getDefaultWorkspaceRoot(): string {
  if (process.platform === 'win32') {
    const appData = process.env.APPDATA ?? path.join(os.homedir(), 'AppData', 'Roaming');
    return path.join(appData, 'efvibe');
  }

  return path.join(os.homedir(), '.efvibe');
}

function sanitizeFolderName(name: string): string {
  const sanitized = name.replace(INVALID_CHARS, '_').trim();
  return sanitized.length > 0 ? sanitized : 'project';
}

export function getProjectSessionFolderName(projectCsprojPath: string): string {
  const base = path.basename(projectCsprojPath, '.csproj');
  return sanitizeFolderName(base || 'project');
}

export function getDbContextSessionFolderName(dbContextName: string): string {
  return sanitizeFolderName(dbContextName || 'DbContext');
}

export function getDbContextSessionDirectory(
  workspaceRoot: string,
  projectCsprojPath: string,
  dbContextName: string,
): string {
  const projectFolder = getProjectSessionFolderName(projectCsprojPath);
  const contextFolder = getDbContextSessionFolderName(dbContextName);
  return path.join(workspaceRoot, projectFolder, contextFolder);
}

export function getProjectScanDirectory(workspaceRoot: string, projectCsprojPath: string): string {
  const projectFolder = getProjectSessionFolderName(projectCsprojPath);
  return path.join(workspaceRoot, projectFolder, 'scan');
}

export const LITE_SCAN_FILE_NAME = 'myefvibe-scan-lite.json';
export const DEEP_SCAN_FILE_NAME = 'myefvibe-scan-deep.json';
export const SCAN_DISMISSALS_FILE_NAME = 'myefvibe-scan-dismissals.json';

export function getLiteScanFilePath(workspaceRoot: string, projectCsprojPath: string): string {
  return path.join(getProjectScanDirectory(workspaceRoot, projectCsprojPath), LITE_SCAN_FILE_NAME);
}

export function getDeepScanFilePath(
  workspaceRoot: string,
  projectCsprojPath: string,
  dbContextName: string,
): string {
  return path.join(
    getDbContextSessionDirectory(workspaceRoot, projectCsprojPath, dbContextName),
    DEEP_SCAN_FILE_NAME,
  );
}
