using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using System.Collections.Concurrent;

namespace Arius.Core.Tests.Helpers.FakeLogger;

public class FakeLoggerFactory : ILoggerFactory
{
    private readonly ConcurrentDictionary<string, ILogger> _loggers = new();
    private readonly FakeLogCollector _collector = new();

    public void AddProvider(ILoggerProvider provider)
    {
        // Not needed for FakeLogger, ignore.
    }

    public ILogger CreateLogger(string categoryName)
    {
        // Return an existing FakeLogger or create a new non-generic one
        return _loggers.GetOrAdd(categoryName, _ => new Microsoft.Extensions.Logging.Testing.FakeLogger(_collector, categoryName));
    }

    public void Dispose()
    {
        _loggers.Clear();
    }

    // Convenience helper for generic loggers
    public FakeLogger<T> CreateLogger<T>() =>
        (FakeLogger<T>)_loggers.GetOrAdd(typeof(T).FullName!, _ => new FakeLogger<T>(_collector));

    // Access to the collector for test verification
    public FakeLogCollector LogCollector => _collector;
}