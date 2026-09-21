/**
 * Flatten a C# snippet to one REPL line without collapsing whitespace inside
 * string / char / interpolated / raw literals. VS Code `Terminal.sendText`
 * submits each physical line separately, so newlines outside literals become
 * spaces; interior literal whitespace is preserved.
 */
export function collapseSnippetWhitespace(source: string): string {
  const builder: string[] = [];
  let index = 0;
  let pendingSpace = false;

  const emitSpace = (): void => {
    pendingSpace = true;
  };

  const emit = (text: string): void => {
    if (text.length === 0) {
      return;
    }

    if (pendingSpace && builder.length > 0) {
      builder.push(' ');
    }

    pendingSpace = false;
    builder.push(text);
  };

  while (index < source.length) {
    const literal = tryReadLiteral(source, index);

    if (literal) {
      emit(literal.text);
      index = literal.end;
      continue;
    }

    const character = source[index];

    if (isWhitespace(character)) {
      emitSpace();
      index += 1;
      continue;
    }

    emit(character);
    index += 1;
  }

  return builder.join('').replace(/\r\n/g, ' ').replace(/[\r\n]/g, ' ').trim();
}

export function formatSnippetForReplSubmit(snippet: string): string {
  const collapsed = collapseSnippetWhitespace(snippet);

  if (!collapsed) {
    return '';
  }

  return collapsed.endsWith(';') ? collapsed : `${collapsed};`;
}

function isWhitespace(character: string): boolean {
  return character === ' '
    || character === '\t'
    || character === '\n'
    || character === '\r'
    || character === '\f'
    || character === '\v';
}

function tryReadLiteral(source: string, start: number): { text: string; end: number } | undefined {
  const character = source[start];

  if (character === '\'') {
    return readCharLiteral(source, start);
  }

  if (character === '@' || character === '$' || character === '"') {
    return readStringLiteral(source, start);
  }

  return undefined;
}

function readCharLiteral(source: string, start: number): { text: string; end: number } {
  let index = start + 1;

  while (index < source.length) {
    const character = source[index];

    if (character === '\\') {
      index += 2;
      continue;
    }

    if (character === '\'') {
      return { text: source.slice(start, index + 1), end: index + 1 };
    }

    if (character === '\n' || character === '\r') {
      break;
    }

    index += 1;
  }

  return { text: source.slice(start, index), end: index };
}

function readStringLiteral(source: string, start: number): { text: string; end: number } | undefined {
  let index = start;
  let dollarCount = 0;

  while (index < source.length && source[index] === '$') {
    dollarCount += 1;
    index += 1;
  }

  let verbatim = false;

  if (index < source.length && source[index] === '@') {
    verbatim = true;
    index += 1;
  }

  if (dollarCount === 0 && index < source.length && source[index] === '$') {
    while (index < source.length && source[index] === '$') {
      dollarCount += 1;
      index += 1;
    }
  }

  if (index >= source.length || source[index] !== '"') {
    return undefined;
  }

  const quoteCount = countQuotes(source, index);

  if (quoteCount >= 3) {
    return readRawString(source, start, index, quoteCount, dollarCount);
  }

  if (verbatim) {
    return readVerbatimString(source, start, index + 1, dollarCount);
  }

  return readRegularString(source, start, index + 1, dollarCount);
}

function countQuotes(source: string, start: number): number {
  let count = 0;
  let index = start;

  while (index < source.length && source[index] === '"') {
    count += 1;
    index += 1;
  }

  return count;
}

function readRegularString(
  source: string,
  start: number,
  contentStart: number,
  dollarCount: number,
): { text: string; end: number } {
  const pieces: string[] = [source.slice(start, contentStart)];
  let index = contentStart;

  while (index < source.length) {
    const character = source[index];

    if (character === '\\') {
      pieces.push(source.slice(index, index + 2));
      index += 2;
      continue;
    }

    if (character === '"' ) {
      pieces.push('"');
      return { text: pieces.join(''), end: index + 1 };
    }

    if (dollarCount > 0 && character === '{') {
      const hole = readInterpolationHole(source, index, dollarCount);
      pieces.push(hole.text);
      index = hole.end;
      continue;
    }

    pieces.push(character);
    index += 1;
  }

  return { text: pieces.join(''), end: index };
}

function readVerbatimString(
  source: string,
  start: number,
  contentStart: number,
  dollarCount: number,
): { text: string; end: number } {
  const pieces: string[] = [source.slice(start, contentStart)];
  let index = contentStart;

  while (index < source.length) {
    const character = source[index];

    if (character === '"' && source[index + 1] === '"') {
      pieces.push('""');
      index += 2;
      continue;
    }

    if (character === '"') {
      pieces.push('"');
      return { text: pieces.join(''), end: index + 1 };
    }

    if (dollarCount > 0 && character === '{') {
      const hole = readInterpolationHole(source, index, dollarCount);
      pieces.push(hole.text);
      index = hole.end;
      continue;
    }

    pieces.push(character);
    index += 1;
  }

  return { text: pieces.join(''), end: index };
}

function readRawString(
  source: string,
  start: number,
  quoteStart: number,
  quoteCount: number,
  dollarCount: number,
): { text: string; end: number } {
  const pieces: string[] = [source.slice(start, quoteStart + quoteCount)];
  let index = quoteStart + quoteCount;

  while (index < source.length) {
    if (source[index] === '"') {
      const available = countQuotes(source, index);

      if (available >= quoteCount) {
        pieces.push(source.slice(index, index + quoteCount));
        return { text: pieces.join(''), end: index + quoteCount };
      }
    }

    if (dollarCount > 0 && source[index] === '{') {
      const hole = readInterpolationHole(source, index, dollarCount);
      pieces.push(hole.text);
      index = hole.end;
      continue;
    }

    pieces.push(source[index]);
    index += 1;
  }

  return { text: pieces.join(''), end: index };
}

function readInterpolationHole(
  source: string,
  start: number,
  dollarCount: number,
): { text: string; end: number } {
  let braceCount = 0;

  while (start + braceCount < source.length && source[start + braceCount] === '{') {
    braceCount += 1;
  }

  if (braceCount !== dollarCount) {
    return { text: source.slice(start, start + braceCount), end: start + braceCount };
  }

  const innerStart = start + braceCount;
  let index = innerStart;
  let depth = 1;

  while (index < source.length && depth > 0) {
    const literal = tryReadLiteral(source, index);

    if (literal) {
      index = literal.end;
      continue;
    }

    const character = source[index];

    if (character === '{') {
      depth += 1;
    } else if (character === '}') {
      depth -= 1;

      if (depth === 0) {
        const inner = source.slice(innerStart, index);
        const collapsedInner = collapseSnippetWhitespace(inner);
        return {
          text: `${'{'.repeat(braceCount)}${collapsedInner}${'}'.repeat(braceCount)}`,
          end: index + braceCount,
        };
      }
    }

    index += 1;
  }

  return { text: source.slice(start, index), end: index };
}
