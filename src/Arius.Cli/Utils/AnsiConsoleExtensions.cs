using Spectre.Console;

namespace Arius.Cli.Utils;

internal static class AnsiConsoleExtensions
{
    public static void StartNewRecording()
    {
        AnsiConsole.Record();
        length = AnsiConsole.ExportText().Length;
    }

    private static int length;

    public static string ExportNewText()
    {
        return AnsiConsole.ExportText().Substring(length);
    }
}