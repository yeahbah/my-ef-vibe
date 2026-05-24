import * as vscode from 'vscode';

const CHANNEL_NAME = 'efvibe';

let channel: vscode.OutputChannel | undefined;

export function appendToEfvibeOutput(message: string, show = false): void {
  if (!channel) {
    channel = vscode.window.createOutputChannel(CHANNEL_NAME);
  }

  channel.appendLine(message);

  if (show) {
    channel.show(true);
  }
}

/** First readable line from CLI stderr/stdout (strips Spectre box-drawing). */
export function summarizeCliOutput(stderr: string, stdout: string): string {
  const combined = `${stderr}\n${stdout}`.trim();

  if (!combined) {
    return 'No output from efvibe.';
  }

  const lines = combined
    .split(/\r?\n/u)
    .map((line) => line.replace(/[\u2500-\u257F│╭╮╯╰▀─]+/gu, '').trim())
    .filter((line) => line.length > 0);

  const first = lines[0] ?? combined;
  return first.length > 320 ? `${first.slice(0, 317)}...` : first;
}
