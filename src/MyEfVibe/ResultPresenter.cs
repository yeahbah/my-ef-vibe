using System.Collections;
using System.Text;
using Spectre.Console;

namespace MyEfVibe;

internal static class ResultPresenter
{
    internal static void Present(object? evaluatedProjection, TextWriter? writer = null)
    {
        if (writer is not null && writer != Console.Out)
        {
            PresentPlain(evaluatedProjection, writer);
            return;
        }

        if (Console.IsOutputRedirected)
        {
            PresentPlain(evaluatedProjection, writer ?? Console.Out);
            return;
        }

        PresentStyled(evaluatedProjection);
    }

    private static void PresentStyled(object? evaluatedProjection)
    {
        if (evaluatedProjection is null)
        {
            AnsiConsole.MarkupLine("[grey]<null>[/]");
            return;
        }

        if (evaluatedProjection is string textLiteral)
        {
            WriteResultPanel(textLiteral);
            return;
        }

        if (evaluatedProjection is IQueryable queryable)
        {
            WriteResultPanel(DescribeDeferredQueryable(queryable));
            return;
        }

        if (evaluatedProjection is IEnumerable sequence and not string)
        {
            var builder = new StringBuilder();
            var printedRowCount = 0;

            foreach (var element in sequence)
            {
                builder.AppendLine($"[cyan]•[/] {Markup.Escape(element?.ToString() ?? "<null>")}");
                printedRowCount++;

                if (printedRowCount >= 250)
                {
                    builder.AppendLine("[grey]… (truncated)[/]");
                    break;
                }
            }

            if (printedRowCount == 0)
            {
                AnsiConsole.MarkupLine("[grey](empty)[/]");
                return;
            }

            AnsiConsole.Write(
                new Panel(builder.ToString())
                {
                    Header = new PanelHeader($"[bold]Result[/] [grey]({printedRowCount} rows)[/]"),
                    Border = BoxBorder.Rounded,
                    BorderStyle = new Style(Color.Grey),
                    Padding = new Padding(1, 0, 1, 0)
                });

            AnsiConsole.WriteLine();
            return;
        }

        WriteResultPanel(evaluatedProjection.ToString() ?? string.Empty);
    }

    private static void WriteResultPanel(string content)
    {
        AnsiConsole.Write(
            new Panel(Markup.Escape(content))
            {
                Header = new PanelHeader("[bold]Result[/]"),
                Border = BoxBorder.Rounded,
                BorderStyle = new Style(Color.Grey),
                Padding = new Padding(1, 0, 1, 0)
            });

        AnsiConsole.WriteLine();
    }

    private static void PresentPlain(object? evaluatedProjection, TextWriter writer)
    {
        if (evaluatedProjection is null)
        {
            writer.WriteLine("<null>");
            return;
        }

        if (evaluatedProjection is string textLiteral)
        {
            writer.WriteLine(textLiteral);
            return;
        }

        if (evaluatedProjection is IQueryable queryable)
        {
            writer.WriteLine(DescribeDeferredQueryable(queryable));
            return;
        }

        if (evaluatedProjection is IEnumerable sequence and not string)
        {
            var printedRowCount = 0;

            foreach (var element in sequence)
            {
                writer.WriteLine(element);

                if (++printedRowCount >= 250)
                {
                    writer.WriteLine("...(truncated)");
                    break;
                }
            }

            if (printedRowCount == 0)
            {
                writer.WriteLine("(empty)");
            }

            return;
        }

        writer.WriteLine(evaluatedProjection);
    }

    private static string DescribeDeferredQueryable(IQueryable queryable)
    {
        var elementTypeName = queryable.ElementType.FullName ?? queryable.ElementType.Name;

        return
            $"IQueryable<{elementTypeName}> (deferred; add ToList(), ToArray(), First(), Count(), or another terminal operator to execute)";
    }
}