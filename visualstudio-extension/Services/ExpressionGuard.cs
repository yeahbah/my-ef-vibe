using System;
using System.Text;
using System.Text.RegularExpressions;

namespace MyEfVibe.VisualStudio.Services;

internal static class ExpressionGuard
{
    private static readonly Regex[] BlockedEfPatterns =
    {
        new Regex(@"\bSaveChanges(?:Async)?\s*\(", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"\b(?:Add|AddRange|Update|UpdateRange|Remove|RemoveRange|Attach|AttachRange)\s*\(", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"\bExecuteDelete(?:Async)?\s*\(", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"\bExecuteUpdate(?:Async)?\s*\(", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"\bExecuteSql(?:Raw)?(?:Async)?\s*\(", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"\bDatabase\s*\.\s*(?:ExecuteSql(?:Raw)?|EnsureDeleted|EnsureCreated|Migrate)\s*\(", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"\bFromSqlRaw\s*<", RegexOptions.IgnoreCase | RegexOptions.Compiled),
    };

    private static readonly Regex ScriptReferenceDirective = new Regex(@"^\s*#(?:load|r)\b", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex BlockedSql = new Regex(@"\b(?:DROP|DELETE|INSERT|UPDATE|TRUNCATE|ALTER|CREATE)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SqlApi = new Regex(@"\b(?:ExecuteSql|FromSqlRaw|SqlQuery)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    internal static bool IsReadOnly(string expression, out string reason)
    {
        reason = string.Empty;
        var trimmed = expression.Trim();
        if (trimmed.Length == 0)
        {
            reason = "Expression is empty.";
            return false;
        }

        var stripped = StripComments(trimmed);
        if (ScriptReferenceDirective.IsMatch(stripped))
        {
            reason = "Read-only mode: #load and #r directives are not allowed from guarded UI execution paths.";
            return false;
        }

        foreach (var pattern in BlockedEfPatterns)
        {
            if (!pattern.IsMatch(stripped))
                continue;

            reason = "Read-only mode: SaveChanges, Add/Update/Remove, ExecuteSql, ExecuteDelete/Update, and schema changes are not allowed.";
            return false;
        }

        if (BlockedSql.IsMatch(stripped) && SqlApi.IsMatch(stripped))
        {
            reason = "Destructive SQL keywords are not allowed in ExecuteSql / FromSqlRaw expressions.";
            return false;
        }

        return true;
    }

    /// <summary>
    ///     Remove C# comments without treating <c>//</c> or <c>/*</c> inside string/char literals as comments.
    ///     Naive <c>//.*$</c> stripping turns URL filters like <c>"http://example.com").ExecuteDelete()</c>
    ///     into <c>"http:</c> and lets guarded UI paths execute the hidden mutation.
    /// </summary>
    private static string StripComments(string expression)
    {
        var src = expression;
        var i = 0;
        var output = new StringBuilder(src.Length);

        ReadCode(stopAtUnmatchedBrace: false);
        return output.ToString();

        char Peek(int ahead = 0) => i + ahead < src.Length ? src[i + ahead] : '\0';

        void SkipLineComment()
        {
            i += 2;
            while (i < src.Length && src[i] != '\n' && src[i] != '\r')
            {
                i++;
            }
        }

        void SkipBlockComment()
        {
            i += 2;
            while (i < src.Length && !(src[i] == '*' && i + 1 < src.Length && src[i + 1] == '/'))
            {
                i++;
            }

            if (i < src.Length)
            {
                i += 2;
            }
        }

        void ReadCharLiteral()
        {
            output.Append(src[i]);
            i++;
            if (i < src.Length && src[i] == '\\')
            {
                output.Append(src[i]);
                i++;
                if (i < src.Length)
                {
                    output.Append(src[i]);
                    i++;
                }
            }
            else if (i < src.Length)
            {
                output.Append(src[i]);
                i++;
            }

            if (i < src.Length && src[i] == '\'')
            {
                output.Append(src[i]);
                i++;
            }
        }

        void ReadInterpolationHole()
        {
            output.Append('{');
            i++;
            ReadCode(stopAtUnmatchedBrace: true);
            if (Peek() == '}')
            {
                output.Append('}');
                i++;
            }
        }

        void ReadQuotedString(bool verbatim, bool interpolated)
        {
            output.Append('"');
            i++;

            while (i < src.Length)
            {
                var c = Peek();
                var n = Peek(1);

                if (interpolated && c == '{')
                {
                    if (n == '{')
                    {
                        output.Append("{{");
                        i += 2;
                        continue;
                    }

                    ReadInterpolationHole();
                    continue;
                }

                if (interpolated && c == '}' && n == '}')
                {
                    output.Append("}}");
                    i += 2;
                    continue;
                }

                if (verbatim)
                {
                    if (c == '"' && n == '"')
                    {
                        output.Append("\"\"");
                        i += 2;
                        continue;
                    }

                    if (c == '"')
                    {
                        output.Append('"');
                        i++;
                        return;
                    }

                    output.Append(c);
                    i++;
                    continue;
                }

                if (c == '\\' && n != '\0')
                {
                    output.Append(c).Append(n);
                    i += 2;
                    continue;
                }

                if (c == '"')
                {
                    output.Append('"');
                    i++;
                    return;
                }

                output.Append(c);
                i++;
            }
        }

        void ReadRawString(int quoteCount, int dollars)
        {
            for (var copied = 0; copied < quoteCount; copied++)
            {
                output.Append('"');
                i++;
            }

            var holeOpen = dollars > 0 ? new string('{', dollars) : string.Empty;

            while (i < src.Length)
            {
                if (holeOpen.Length > 0
                    && i + holeOpen.Length <= src.Length
                    && src.IndexOf(holeOpen, i, holeOpen.Length, StringComparison.Ordinal) == i
                    && Peek(dollars) != '{')
                {
                    for (var copied = 0; copied < dollars; copied++)
                    {
                        output.Append('{');
                        i++;
                    }

                    ReadCode(stopAtUnmatchedBrace: true);

                    var closed = 0;
                    while (closed < dollars && Peek() == '}')
                    {
                        output.Append('}');
                        i++;
                        closed++;
                    }

                    continue;
                }

                var quotes = 0;
                while (Peek(quotes) == '"')
                {
                    quotes++;
                }

                if (quotes >= quoteCount)
                {
                    for (var copied = 0; copied < quoteCount; copied++)
                    {
                        output.Append('"');
                        i++;
                    }

                    return;
                }

                output.Append(Peek());
                i++;
            }
        }

        bool TryReadString()
        {
            var j = i;
            var dollars = 0;
            var verbatim = false;

            if (j < src.Length && src[j] == '@' && j + 1 < src.Length && src[j + 1] == '$')
            {
                verbatim = true;
                j += 2;
                dollars = 1;
                while (j < src.Length && src[j] == '$')
                {
                    dollars++;
                    j++;
                }
            }
            else
            {
                while (j < src.Length && src[j] == '$')
                {
                    dollars++;
                    j++;
                }

                if (j < src.Length && src[j] == '@')
                {
                    verbatim = true;
                    j++;
                }
            }

            if (j >= src.Length || src[j] != '"')
            {
                return false;
            }

            var quoteCount = 0;
            while (j + quoteCount < src.Length && src[j + quoteCount] == '"')
            {
                quoteCount++;
            }

            while (i < j)
            {
                output.Append(src[i]);
                i++;
            }

            if (quoteCount >= 3)
            {
                ReadRawString(quoteCount, dollars);
                return true;
            }

            ReadQuotedString(verbatim, dollars > 0);
            return true;
        }

        void ReadCode(bool stopAtUnmatchedBrace)
        {
            var braceDepth = 0;

            while (i < src.Length)
            {
                var c = Peek();
                var n = Peek(1);

                if (stopAtUnmatchedBrace && c == '}' && braceDepth == 0)
                {
                    return;
                }

                if (c == '/' && n == '/')
                {
                    SkipLineComment();
                    output.Append(' ');
                    continue;
                }

                if (c == '/' && n == '*')
                {
                    SkipBlockComment();
                    output.Append(' ');
                    continue;
                }

                if (c == '\'')
                {
                    ReadCharLiteral();
                    continue;
                }

                if (TryReadString())
                {
                    continue;
                }

                if (c == '{')
                {
                    braceDepth++;
                }
                else if (c == '}' && braceDepth > 0)
                {
                    braceDepth--;
                }

                output.Append(c);
                i++;
            }
        }
    }
}
