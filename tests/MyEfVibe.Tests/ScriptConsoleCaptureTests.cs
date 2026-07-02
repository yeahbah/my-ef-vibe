namespace MyEfVibe.Tests;

[Collection(ConsoleTestCollection.Name)]
public sealed class ScriptConsoleCaptureTests
{
    [Fact]
    public void Capture_redirects_console_output()
    {
        var previous = Console.Out;

        try
        {
            using var capture = new ScriptConsoleCapture();
            Console.WriteLine("hello");
            Console.WriteLine("world");

            Assert.Equal($"hello{Environment.NewLine}world", capture.CapturedOutput);
        }
        finally
        {
            Console.SetOut(previous);
        }
    }
}
