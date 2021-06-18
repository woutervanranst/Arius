using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading;
using Karambolo.Extensions.Logging.File;
using Microsoft.Extensions.Logging;

namespace Arius.Cli.Extensions
{
    // https://github.com/adams85/filelogger/tree/5263fa0424f1fed0a1fddc7b0f7454fb59e27c53/samples/CustomFormat

    [DebuggerStepThroughAttribute]
    internal class SingleLineLogEntryTextBuilder : FileLogEntryTextBuilder
    {
        public static readonly SingleLineLogEntryTextBuilder Default = new SingleLineLogEntryTextBuilder();

        public override void BuildEntryText(StringBuilder sb, string categoryName, LogLevel logLevel, EventId eventId, string message, Exception exception,
            IExternalScopeProvider scopeProvider, DateTimeOffset timestamp)
        {
            AppendTimestamp(sb, timestamp);

            AppendLogLevel(sb, logLevel);

            AppendCategoryName(sb, categoryName);

            AppendEventId(sb, eventId);

            if (scopeProvider != null)
                AppendLogScopeInfo(sb, scopeProvider);

            if (!string.IsNullOrEmpty(message))
                AppendMessage(sb, message);

            if (exception != null)
                AppendException(sb, exception);
        }

        protected override void AppendTimestamp(StringBuilder sb, DateTimeOffset timestamp)
        {
            sb.Append(timestamp.ToLocalTime().ToString("o", CultureInfo.InvariantCulture));

            sb.Append(" [ThreadId ");
            sb.Append(string.Format($"{Thread.CurrentThread.ManagedThreadId:000}"));
            sb.Append("] ");
        }

        protected override void AppendLogScopeInfo(StringBuilder sb, IExternalScopeProvider scopeProvider)
        {
            scopeProvider.ForEachScope((scope, builder) =>
            {
                builder.Append(' ');

                AppendLogScope(builder, scope);
            }, sb);
        }

        protected override void AppendMessage(StringBuilder sb, string message)
        {
            sb.Append(" => ");

            var length = sb.Length;
            sb.AppendLine(message);
            sb.Replace(Environment.NewLine, " ", length, message.Length);
        }
    }
}