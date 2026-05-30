using System.Text;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace MyEfVibe;

internal static class StartupBanner
{
    private const int RainbowSegmentWidth = 8;

    private static readonly string[] RainbowPalette =
    [
        "blue",
        "yellow",
        "magenta",
        "cyan",
        "green",
        "red"
    ];

    private static readonly string[] TitleLines =
    [
        " ███╗   ███╗██╗   ██╗     ███████╗ ███████╗     ██╗   ██╗██╗██████╗ ███████╗",
        " ████╗ ████║╚██╗ ██╔╝     ██╔════╝ ██╔════╝     ██║   ██║██║██╔══██╗██╔════╝",
        " ██╔████╔██║ ╚████╔╝      █████╗   ███████╗     ██║   ██║██║██████╔╝█████╗  ",
        " ██║╚██╔╝██║  ╚██╔╝       ██╔══╝   ██╔════╝     ╚██╗ ██╔╝██║██╔══██╗██╔══╝  ",
        " ██║ ╚═╝ ██║   ██║        ███████╗ ██║          ╚████╔╝ ██║██████╔╝███████╗ ",
        " ╚═╝     ╚═╝   ╚═╝        ╚══════╝ ╚═╝           ╚═══╝  ╚═╝╚═════╝ ╚══════╝ "
    ];

    private static readonly string[] UnicornLines =
    [
        "                      . . . .",
        "                      ,`,`,`,`,",
        ". . . .               `\\`\\`\\`\\`;",
        "`\\`\\`\\`\\`\\`,            ~|;!;!;\\!",
        " ~\\;\\;\\;\\|\\          (--,!!!~`!       .",
        "(--,\\\\\\===~\\         (--,|||~`!     ./",
        " (--,\\\\\\===~\\         `,-,~,=,:. _,//",
        "  (--,\\\\\\==~`\\        ~-=~-.---|\\;/J,",
        "   (--,\\\\\\((```==.    ~'`~/       a |",
        "     (-,.\\\\('('(`\\\\.  ~'=~|     \\_.  \\",
        "        (,--(,(,(,'\\\\. ~'=|       \\\\_;>",
        "          (,-( ,(,(,;\\\\ ~=/        \\",
        "          (,-/ (.(.(,;\\\\,/          )",
        "           (,--/,;,;,;,\\\\         ./------.",
        "             (==,-;-'`;'         /_,----`. \\",
        "     ,.--_,__.-'                    `--.  ` \\",
        "    (='~-_,--/        ,       ,!,___--. \\  \\_)",
        "   (-/~(     |         \\   ,_-         | ) /_|",
        "   (~/((\\    )\\._,      |-'         _,/ /",
        "    \\\\))))  /   ./~.    |           \\_\\;",
        " ,__/////  /   /    )  /",
        "  '===~'   |  |    (, <.",
        "           / /       \\. \\",
        "         _/ /          \\_\\",
        "        /_!/            >_\\"
    ];

    internal static void Write()
    {
        if (Console.IsOutputRedirected)
        {
            WritePlain();
            return;
        }

        var content = BuildBannerContent();

        AnsiConsole.Write(
            new Panel(content)
            {
                Border = BoxBorder.Rounded,
                BorderStyle = new Style(Color.Grey),
                Padding = new Padding(1, 0, 1, 0)
            });

        AnsiConsole.WriteLine();
    }

    private static IRenderable BuildBannerContent()
    {
        var rows = new List<IRenderable>();

        for (var lineIndex = 0; lineIndex < TitleLines.Length; lineIndex++)
        {
            rows.Add(new Markup(ToRainbowTitleMarkup(TitleLines[lineIndex], lineIndex)));
        }

        rows.Add(Text.Empty);

        for (var index = 0; index < UnicornLines.Length; index++)
        {
            rows.Add(new Markup(ToUnicornLineMarkup(UnicornLines[index], index)));
        }

        return new Rows([.. rows]);
    }

    private static string ToRainbowTitleMarkup(string line, int lineIndex)
    {
        var builder = new StringBuilder();
        var paletteIndex = lineIndex % RainbowPalette.Length;

        for (var offset = 0; offset < line.Length; offset += RainbowSegmentWidth)
        {
            var length = Math.Min(RainbowSegmentWidth, line.Length - offset);
            var segment = line[offset..(offset + length)];
            var color = RainbowPalette[paletteIndex % RainbowPalette.Length];

            paletteIndex++;

            builder.Append($"[bold {color}]");
            builder.Append(Markup.Escape(segment));
            builder.Append("[/]");
        }

        return builder.ToString();
    }

    private static string ToUnicornLineMarkup(string line, int index)
    {
        var color = index switch
        {
            <= 2 => "yellow",
            <= 5 => "magenta",
            <= 10 => "cyan",
            <= 16 => "white",
            _ => "green"
        };

        return $"[{color}]{Markup.Escape(line)}[/]";
    }

    private static void WritePlain()
    {
        Console.WriteLine();

        foreach (var line in TitleLines)
        {
            Console.WriteLine(line);
        }

        Console.WriteLine();

        foreach (var line in UnicornLines)
        {
            Console.WriteLine(line);
        }
    }
}