using System.Collections.Concurrent;
using ConcurrentCollections;
using System.Runtime.CompilerServices;

/// <summary>
/// A thread-safe collection of tasks that are consumed as they complete.
/// Tasks can be added concurrently by multiple producers and consumed by multiple consumers.
/// The collection processes tasks in the order they complete, regardless of the order they were added.
/// </summary>
/// <typeparam name="T">The type of the result returned by the tasks.</typeparam>
internal sealed class ConcurrentConsumingTaskCollection<T>
{
    /// <summary>
    /// A thread-safe hash set that stores tasks to be processed.
    /// </summary>
    private readonly ConcurrentHashSet<Task<T>> taskSet = [];

    /// <summary>
    /// A TaskCompletionSource that signals when no more tasks will be added.
    /// </summary>
    private readonly TaskCompletionSource addingCompletedTcs = new();

    /// <summary>
    /// Adds a task to the collection.
    /// Tasks can be added concurrently, but once <see cref="CompleteAdding"/> is called, no more tasks can be added.
    /// </summary>
    /// <param name="task">The task to be added to the collection.</param>
    /// <exception cref="InvalidOperationException">Thrown if the task is added after adding is completed.</exception>
    public void Add(Task<T> task)
    {
        if (addingCompletedTcs.Task.IsCompleted)
        {
            throw new InvalidOperationException("Cannot add tasks after adding is completed.");
        }
        taskSet.Add(task);  // Add the task in a thread-safe manner
    }

    /// <summary>
    /// Marks the collection as complete, signaling that no more tasks will be added.
    /// Once this method is called, no additional tasks can be added to the collection.
    /// </summary>
    public void CompleteAdding()
    {
        addingCompletedTcs.TrySetResult();  // Signal that no more tasks will be added
    }

    /// <summary>
    /// Determines whether the collection has completed processing.
    /// The collection is considered complete when no more tasks will be added 
    /// and all tasks have been consumed.
    /// </summary>
    public bool IsCompleted => addingCompletedTcs.Task.IsCompleted && taskSet.IsEmpty;

    /// <summary>
    /// Consumes tasks from the collection as they complete.
    /// The tasks are returned in the order of their completion, regardless of the order they were added.
    /// The method stops consuming once all tasks have been processed and no more tasks will be added.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token that can be used to stop consuming tasks.</param>
    /// <returns>An asynchronous enumerable of task results.</returns>
    public async IAsyncEnumerable<T> GetConsumingEnumerable([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        while (!IsCompleted)
        {
            // If there are no more tasks being added and the set is empty, we stop processing
            if (taskSet.IsEmpty && addingCompletedTcs.Task.IsCompleted)
                yield break;

            Task completedTask;

            if (taskSet.Any())
            {
                // Wait for either a task from the set to complete or the TaskCompletionSource to complete
                completedTask = await Task.WhenAny(taskSet.Append(addingCompletedTcs.Task));
            }
            else
            {
                // If no tasks are in the set, check if new tasks might still be added
                if (addingCompletedTcs.Task.IsCompleted)
                {
                    // No tasks and no more will be added, stop processing
                    yield break;
                }

                // No tasks in the set, but more might be added, yield and continue to the next iteration
                await Task.Yield();
                continue;
            }

            // If it's a task from the set that completed, process it
            if (completedTask is Task<T> completedResult && taskSet.TryRemove(completedResult))
            {
                yield return await completedResult;
            }

            // Optionally exit if cancellation is requested
            if (cancellationToken.IsCancellationRequested)
            {
                yield break;
            }
        }
    }
}
