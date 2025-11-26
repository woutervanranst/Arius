namespace Arius.Cli.CliCommands;

internal static class StringExtensions
{
    extension(string input)
    {
        public string TruncateAndRightJustify(int width)
        {
            if (width <= 0) return string.Empty;
            const string ellipsis     = "...";
            var          contentWidth = width - ellipsis.Length;
            if (contentWidth <= 0) return ellipsis[..width];
            string truncated = input.Length > contentWidth ? ellipsis + input[^contentWidth..] : input;
            return truncated.PadLeft(width);
        }

        public string TruncateAndLeftJustify(int width)
        {
            if (width <= 0) return string.Empty;
            var truncated = input.Length > width ? input[..width] : input;
            return truncated.PadRight(width);
        }
    }
}