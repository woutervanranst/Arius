using System.Collections.Concurrent;

namespace Arius.Core.Shared.Concurrency;

/// <summary>
/// Single-flight (request coalescing) gate: the first caller per key performs the work,
/// concurrent callers await the same result.
/// </summary>
internal sealed class InFlightGate<TKey, TResult> where TKey : notnull
{
    private readonly ConcurrentDictionary<TKey, TaskCompletionSource<TResult>> _inFlight = new();

    /// <summary>
    /// Enter for a key. If first: isOwner = true (you must do the work). Otherwise: await waitTask.
    /// </summary>
    public (bool isOwner, Task<TResult> waitTask) Enter(TKey key)
    {
        var tcs = new TaskCompletionSource<TResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var existing = _inFlight.GetOrAdd(key, tcs);
        return (ReferenceEquals(existing, tcs), existing.Task);
    }

    public void Complete(TKey key, TResult result)
    {
        if (_inFlight.TryRemove(key, out var tcs))
            tcs.TrySetResult(result);
    }

    public void Fault(TKey key, Exception ex)
    {
        if (_inFlight.TryRemove(key, out var tcs))
            tcs.TrySetException(ex);
    }

    public void Cancel(TKey key, CancellationToken ct = default)
    {
        if (_inFlight.TryRemove(key, out var tcs))
            tcs.TrySetCanceled(ct);
    }
}