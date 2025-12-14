namespace Arius.Core.Shared.Extensions;

internal static class TaskExtensions
{
    extension(IEnumerable<Task> tasks)
    {
        /// <summary>
        /// Raise the Cancellation when any of the given tasks fault.
        /// </summary>
        public void RaiseCancellationOnFault(CancellationTokenSource cts)
        {
            if (tasks == null)
                throw new ArgumentNullException(nameof(tasks));
            if (cts == null)
                throw new ArgumentNullException(nameof(cts));

            foreach (var task in tasks)
            {
                if (task == null)
                    continue;

                task.ContinueWith(t =>
                    {
                        if (t.IsFaulted && !cts.IsCancellationRequested)
                        {
                            cts.Cancel();
                        }
                    }, CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
        }
    }
}