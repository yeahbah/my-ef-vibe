namespace MyEfVibe;

using System.Globalization;

internal sealed record ScriptAttributedBlock(
    string Attribute,
    string Code,
    int LineNumber,
    string? Parameter = null);

internal static class ScriptAttributeParser
{
    internal static bool TryGetCompareBlocks(string source, out IReadOnlyList<ScriptAttributedBlock> blocks)
    {
        blocks = Parse(source)
            .Where(static block => block.Attribute.Equals("Compare", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return blocks.Count > 0;
    }

    internal static bool TryGetBenchmarkBlock(string source, out ScriptAttributedBlock? block)
    {
        block = Parse(source)
            .FirstOrDefault(static candidate =>
                candidate.Attribute.Equals("Benchmark", StringComparison.OrdinalIgnoreCase));

        return block is not null;
    }

    internal static int GetBenchmarkIterations(ScriptAttributedBlock block, int defaultIterations = 5)
    {
        if (string.IsNullOrWhiteSpace(block.Parameter))
        {
            return defaultIterations;
        }

        return int.TryParse(block.Parameter, NumberStyles.Integer, CultureInfo.InvariantCulture, out var iterations)
               && iterations >= 1
            ? iterations
            : defaultIterations;
    }

    internal static bool IsScriptAttributeLine(string line)
    {
        return TryParseAttributeLine(line, out _, out _);
    }

    internal static bool ContainsScriptAttributeLines(string source)
    {
        return InputLineUtilities.SplitLines(source).Any(IsScriptAttributeLine);
    }

    internal static string StripScriptAttributeLines(string source)
    {
        var lines = InputLineUtilities.SplitLines(source);
        var kept = lines.Where(static line => !IsScriptAttributeLine(line)).ToArray();

        return kept.Length == 0 ? string.Empty : InputLineUtilities.JoinLines(kept).Trim();
    }

    internal static IReadOnlyList<ScriptAttributedBlock> Parse(string source)
    {
        var lines = InputLineUtilities.SplitLines(source);
        var preamble = new List<string>();
        var blocks = new List<ScriptAttributedBlock>();
        var body = new List<string>();
        string? currentAttribute = null;
        string? currentParameter = null;
        var currentLine = 0;

        void FlushBlock()
        {
            if (currentAttribute is null)
            {
                return;
            }

            var code = JoinPreambleAndBody(preamble, body);

            if (!string.IsNullOrWhiteSpace(code))
            {
                blocks.Add(new ScriptAttributedBlock(currentAttribute, code, currentLine, currentParameter));
            }

            body.Clear();
            currentAttribute = null;
            currentParameter = null;
        }

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];

            if (TryParseAttributeLine(line, out var attributeName, out var parameter))
            {
                FlushBlock();
                currentAttribute = attributeName;
                currentParameter = parameter;
                currentLine = index + 1;
                continue;
            }

            if (currentAttribute is null)
            {
                preamble.Add(line);
            }
            else
            {
                body.Add(line);
            }
        }

        FlushBlock();

        return blocks;
    }

    private static string JoinPreambleAndBody(IReadOnlyList<string> preamble, IReadOnlyList<string> body)
    {
        var preambleText = string.Join('\n', preamble).Trim();
        var bodyText = string.Join('\n', body).Trim();

        if (string.IsNullOrEmpty(preambleText))
        {
            return bodyText;
        }

        if (string.IsNullOrEmpty(bodyText))
        {
            return preambleText;
        }

        return $"{preambleText}\n{bodyText}";
    }

    private static bool TryParseAttributeLine(string line, out string attributeName, out string? parameter)
    {
        attributeName = string.Empty;
        parameter = null;
        var trimmed = line.Trim();

        if (!trimmed.StartsWith("#[", StringComparison.Ordinal) || !trimmed.EndsWith(']'))
        {
            return false;
        }

        var inner = trimmed[2..^1].Trim();
        var openParen = inner.IndexOf('(');

        if (openParen >= 0)
        {
            attributeName = inner[..openParen].Trim();

            if (inner.EndsWith(')'))
            {
                var args = inner[(openParen + 1)..^1].Trim();

                if (args.Length > 0)
                {
                    parameter = TryParseStringParameter(args, out var stringValue)
                        ? stringValue
                        : args;
                }
            }
        }
        else
        {
            attributeName = inner;
        }

        return !string.IsNullOrWhiteSpace(attributeName);
    }

    private static bool TryParseStringParameter(string args, out string? value)
    {
        value = null;

        if (args.Length < 2)
        {
            return false;
        }

        var quote = args[0];

        if (quote != '"' && quote != '\'')
        {
            return false;
        }

        if (args[^1] != quote)
        {
            return false;
        }

        value = args[1..^1]
            .Replace("\\\"", "\"", StringComparison.Ordinal)
            .Replace("\\'", "'", StringComparison.Ordinal)
            .Replace("\\\\", "\\", StringComparison.Ordinal);

        return true;
    }
}
