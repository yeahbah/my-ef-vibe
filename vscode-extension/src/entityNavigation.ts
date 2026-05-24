import * as path from 'path';
import * as vscode from 'vscode';

export async function goToEntityDefinition(
  entityTypeName: string,
  entityTypeFullName: string | undefined,
  searchRoots: string[],
): Promise<void> {
  const simpleName = entityTypeName.trim();

  if (!simpleName) {
    void vscode.window.showWarningMessage('efvibe: No entity type name to navigate to.');
    return;
  }

  const symbolMatch = await findWorkspaceSymbol(simpleName, entityTypeFullName);

  if (symbolMatch) {
    await openAtRange(symbolMatch.uri, symbolMatch.range);
    return;
  }

  const fileMatch = await findEntitySourceFile(simpleName, searchRoots);

  if (fileMatch) {
    await openAtRange(fileMatch.uri, fileMatch.range);
    return;
  }

  void vscode.window.showWarningMessage(
    `efvibe: Could not find source for entity type ${simpleName}. Install the C# extension or open the entity file manually.`,
  );
}

async function openAtRange(uri: vscode.Uri, range: vscode.Range): Promise<void> {
  const document = await vscode.workspace.openTextDocument(uri);
  const editor = await vscode.window.showTextDocument(document, {
    viewColumn: vscode.ViewColumn.One,
    preserveFocus: false,
  });
  editor.selection = new vscode.Selection(range.start, range.start);
  editor.revealRange(range, vscode.TextEditorRevealType.InCenter);
}

async function findWorkspaceSymbol(
  simpleName: string,
  entityTypeFullName: string | undefined,
): Promise<{ uri: vscode.Uri; range: vscode.Range } | undefined> {
  const queries = [entityTypeFullName, simpleName].filter((entry): entry is string => Boolean(entry?.trim()));

  for (const query of queries) {
    const symbols = await vscode.commands.executeCommand<vscode.SymbolInformation[] | undefined>(
      'vscode.executeWorkspaceSymbolProvider',
      query,
    );

    if (!symbols?.length) {
      continue;
    }

    const matches = symbols.filter((symbol) => isEntitySymbol(symbol, simpleName, entityTypeFullName));

    if (matches.length === 1) {
      return { uri: matches[0].location.uri, range: matches[0].location.range };
    }

    if (matches.length > 1) {
      const picked = await vscode.window.showQuickPick(
        matches.map((symbol) => ({
          label: symbol.name,
          description: symbol.containerName,
          detail: symbol.location.uri.fsPath,
          symbol,
        })),
        { title: `Go to ${simpleName}` },
      );

      if (picked) {
        return { uri: picked.symbol.location.uri, range: picked.symbol.location.range };
      }

      return undefined;
    }
  }

  return undefined;
}

function isEntitySymbol(
  symbol: vscode.SymbolInformation,
  simpleName: string,
  entityTypeFullName: string | undefined,
): boolean {
  if (symbol.kind !== vscode.SymbolKind.Class && symbol.kind !== vscode.SymbolKind.Struct) {
    return false;
  }

  if (symbol.name.localeCompare(simpleName, undefined, { sensitivity: 'accent' }) === 0) {
    return true;
  }

  if (entityTypeFullName) {
    const suffix = `.${symbol.name}`;
    return entityTypeFullName.endsWith(suffix)
      || entityTypeFullName.localeCompare(simpleName, undefined, { sensitivity: 'accent' }) === 0;
  }

  return false;
}

async function findEntitySourceFile(
  simpleName: string,
  searchRoots: string[],
): Promise<{ uri: vscode.Uri; range: vscode.Range } | undefined> {
  const declarationPattern = new RegExp(
    `(?:^|\\s)(?:public\\s+|internal\\s+|private\\s+|protected\\s+)?(?:partial\\s+)?(?:class|struct|record)\\s+${escapeRegExp(simpleName)}\\b`,
    'm',
  );

  for (const root of searchRoots) {
    const candidates = new Map<string, vscode.Uri>();

    for (const uri of await vscode.workspace.findFiles(
      new vscode.RelativePattern(root, `**/${simpleName}.cs`),
      '**/bin/**,**/obj/**',
      20,
    )) {
      candidates.set(uri.fsPath, uri);
    }

    for (const uri of await vscode.workspace.findFiles(
      new vscode.RelativePattern(root, '**/*.cs'),
      '**/bin/**,**/obj/**,**/node_modules/**',
      150,
    )) {
      if (path.basename(uri.fsPath).includes(simpleName, undefined)) {
        candidates.set(uri.fsPath, uri);
      }
    }

    for (const uri of candidates.values()) {
      const document = await vscode.workspace.openTextDocument(uri);
      const match = declarationPattern.exec(document.getText());

      if (!match || match.index === undefined) {
        continue;
      }

      const position = document.positionAt(match.index + match[0].lastIndexOf(simpleName));
      return { uri, range: new vscode.Range(position, position) };
    }
  }

  return undefined;
}

function escapeRegExp(value: string): string {
  return value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}
