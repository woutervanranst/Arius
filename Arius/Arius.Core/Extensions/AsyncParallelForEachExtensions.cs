using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace Arius.Core.Commands.Archive;

[DebuggerStepThrough]
internal static class AsyncParallelForEachExtensions
{
    // https://scatteredcode.net/parallel-foreach-async-in-c/

    public static async Task AsyncParallelForEachAsync<T>(this IAsyncEnumerable<T> source, Func<T, Task> body, int maxDegreeOfParallelism = DataflowBlockOptions.Unbounded, TaskScheduler scheduler = null)
    {
        var options = new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = maxDegreeOfParallelism };

        if (scheduler != null)
            options.TaskScheduler = scheduler;

        var block = new ActionBlock<T>(body, options);
        await foreach (var item in source)
            block.Post(item);

        block.Complete();

        await block.Completion;
    }

    public static Task AsyncParallelForEachAsync<T>(this IEnumerable<T> source, Func<T, Task> body, int maxDegreeOfParallelism = DataflowBlockOptions.Unbounded, TaskScheduler scheduler = null)
    {
        var options = new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = maxDegreeOfParallelism };

        if (scheduler != null)
            options.TaskScheduler = scheduler;

        var block = new ActionBlock<T>(body, options);
        foreach (var item in source)
            block.Post(item);
            
        block.Complete();
            
        return block.Completion;
    }


    public static async Task AsyncParallelForEachAsync<T>(this BlockingCollection<T> source, Func<T, Task> body, int degreeOfParallelism)
    {
        // FROM https://stackoverflow.com/a/14678329/1582323 with GetConsumingPartitioner()

        var partitions = source.GetConsumingPartitioner().GetPartitions(degreeOfParallelism);
        var tasks = partitions.Select(async (partition) =>
        {
            using (partition)
                while (partition.MoveNext())
                    await body(partition.Current);
        });

        await Task.WhenAll(tasks);
    }
}