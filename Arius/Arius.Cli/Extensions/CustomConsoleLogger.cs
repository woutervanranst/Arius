using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using System;
using System.IO;

namespace Arius.Cli.Extensions
{
    public static class ConsoleLoggerExtensions
    {
        public static ILoggingBuilder AddCustomFormatter(
            this ILoggingBuilder builder,
            Action<CustomOptions> configure) =>
            builder.AddConsole(options => options.FormatterName = "customName")
                .AddConsoleFormatter<CustomFormatter, CustomOptions>(configure);
    }

    public class CustomOptions : ConsoleFormatterOptions
    {
        public string CustomPrefix { get; set; }
    }

    public sealed class CustomFormatter : ConsoleFormatter, IDisposable
    {
        private readonly IDisposable _optionsReloadToken;
        private CustomOptions _formatterOptions;

        public CustomFormatter(IOptionsMonitor<CustomOptions> options)
            // Case insensitive
            : base("customName") =>
            (_optionsReloadToken, _formatterOptions) =
                (options.OnChange(ReloadLoggerOptions), options.CurrentValue);

        private void ReloadLoggerOptions(CustomOptions options) =>
            _formatterOptions = options;

        public override void Write<TState>(
            in LogEntry<TState> logEntry,
            IExternalScopeProvider scopeProvider,
            TextWriter textWriter)
        {
            if (logEntry.Exception is null)
            {
                return;
            }

            string message =
                logEntry.Formatter(
                    logEntry.State, logEntry.Exception);

            if (message == null)
            {
                return;
            }

            CustomLogicGoesHere(textWriter);
            textWriter.WriteLine(message);
        }

        private void CustomLogicGoesHere(TextWriter textWriter)
        {
            textWriter.Write(_formatterOptions.CustomPrefix);
        }

        public void Dispose() => _optionsReloadToken?.Dispose();
    }
}