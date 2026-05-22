export interface EvaluationJsonMetrics {
  totalMs: number;
  databaseMs?: number;
  rowCount?: number;
  sqlCommandCount: number;
  resultKind: string;
  estimatedBytes?: number;
}

export interface EvaluationJsonPayload {
  success: boolean;
  value?: string | null;
  rows?: Array<Record<string, string>>;
  sql: string[];
  translatedSql?: string;
  metrics: EvaluationJsonMetrics;
  warnings: string[];
  error?: string;
  snippet?: string;
}

export function parseEvaluationJson(stdout: string): EvaluationJsonPayload | undefined {
  const line = stdout
    .split(/\r?\n/u)
    .map((entry) => entry.trim())
    .find((entry) => entry.startsWith('{'));

  if (!line) {
    return undefined;
  }

  try {
    return JSON.parse(line) as EvaluationJsonPayload;
  } catch {
    return undefined;
  }
}
