const DOCS_BASE = 'https://myefvibe.com/docs/scan.html';

export function getScanRuleDocsUrl(ruleId: string | undefined): string | undefined {
  if (!ruleId?.trim()) {
    return undefined;
  }

  return DOCS_BASE;
}

export function appendLearnMoreMarkdown(message: string, ruleId: string | undefined): string {
  const url = getScanRuleDocsUrl(ruleId);

  if (!url) {
    return message;
  }

  return `${message}\n\n[Learn more about \`${ruleId}\`](${url})`;
}
