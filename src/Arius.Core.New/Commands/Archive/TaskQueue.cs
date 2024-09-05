using ConcurrentCollections;
using System.Runtime.CompilerServices;

internal sealed class TaskQueue<T>
{
    private readonly ConcurrentHashSet<Task<T>> _taskSet           = new();  // Thread-safe hash set to hold tasks
    private          bool                       _isAddingCompleted = false;  // Indicates if CompleteAdding has been called
    private readonly object                     _lock              = new();  // For thread-safe state changes

    // Add tasks to the queue (thread-safe)
    public void Add(Task<T> task)
    {
        if (IsAddingCompleted)
        {
            throw new InvalidOperationException("Cannot add tasks after adding is completed.");
        }
        _taskSet.Add(task);  // Add the task in a thread-safe manner
    }

    // Complete adding tasks
    public void CompleteAdding()
    {
        lock (_lock)
        {
            _isAddingCompleted = true;
        }
    }

    // Check if adding is completed
    public bool IsAddingCompleted
    {
        get
        {
            lock (_lock)
            {
                return _isAddingCompleted;
            }
        }
    }

    // Check if the queue has finished processing (no more tasks will be added and all tasks have been consumed)
    public bool IsCompleted
    {
        get
        {
            lock (_lock)
            {
                return _isAddingCompleted && _taskSet.IsEmpty;
            }
        }
    }

    // Consume tasks as they complete, returning only completed tasks in completion order
    public async IAsyncEnumerable<T> ConsumingEnumerable([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        while (!IsCompleted)
        {
            var completedTask = await GetFirstCompletedTaskAsync();

            if (completedTask is not null)
            {
                // Remove the completed task from the set and process it atomically
                if (_taskSet.TryRemove(completedTask))
                {
                    // Yield the result of the completed task
                    yield return await completedTask;
                }
            }

            // add yield?

            // Optionally exit if cancellation is requested
            if (cancellationToken.IsCancellationRequested)
            {
                yield break;
            }
        }
    }

    // Find the first completed task from the set
    private async Task<Task<T>?> GetFirstCompletedTaskAsync()
    {
        if (_taskSet.IsEmpty)
            return null;

        return await Task.WhenAny(_taskSet);
    }
}
