using Microsoft.Extensions.Logging.Testing;

namespace Arius.Core.Tests.Helpers.FakeLogger;

public static class FakeLoggerFactoryExtensions
{
    public static FakeLogRecord? GetLogRecordByTemplate(this FakeLoggerFactory factory, string template)
    {
        return factory.LogCollector.GetSnapshot().SingleOrDefaultBy(template);
    }
}

public static class FakeLogRecordExtensions
{
    public static FakeLogRecord? SingleOrDefaultBy(this IReadOnlyList<FakeLogRecord> records, string originalFormat)
    {
        if (records is null) throw new ArgumentNullException(nameof(records));
        if (originalFormat is null) throw new ArgumentNullException(nameof(originalFormat));

        return records.SingleOrDefault(r =>
            r.StructuredState != null &&
            r.StructuredState.Any(kvp =>
                (kvp.Key == "{OriginalFormat}" || kvp.Key == "OriginalFormat") &&
                string.Equals(kvp.Value?.ToString(), originalFormat, StringComparison.Ordinal)));

    }
}