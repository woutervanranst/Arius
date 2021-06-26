using Arius.Core.Models;
using Microsoft.Extensions.Logging;
using Nito.AsyncEx;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Arius.Core.Commands
{
    internal abstract class BlockBase
    {
        public static IEnumerable<Task> AllTasks => tasks.AsEnumerable();
        protected static readonly ConcurrentBag<Task> tasks = new();

        protected static readonly CancellationTokenSource cts = new();
        protected static readonly CancellationToken ct = cts.Token;
        public static Task CancellationTask => new CancellationTokenTaskSource<object>(ct).Task;
    }

    internal abstract class TaskBlockBase<TSource> : BlockBase
    {
        protected TaskBlockBase(ILogger logger, TSource source, Action done)
        {
            this.logger = logger;

            this.source = source;

            this.done = () =>
            {
                logger.LogTrace("Done");
                done();
            };

            this.task = new(() =>
            {
                var t = TaskImpl();

                // When one task has an exception, set the CancellationToken to all other Tasks
                //NOTE do not put this on one line, ContinueWith returns another task that will not be faulted
                var t2 = t.ContinueWith(t => cts.Cancel(), TaskContinuationOptions.OnlyOnFaulted); 
                
                tasks.Add(t);

                logger.LogTrace($"Created {t.Id}");

                return t;
            }, isThreadSafe: false);
        }

        private readonly TSource source;
        protected readonly ILogger logger;
        private readonly Lazy<Task> task;
        private readonly Action done;

        public Task GetTask => task.Value;

        private Task TaskImpl()
        {
            return Task.Run(async () =>
            {
                try
                {
                    logger.LogTrace("Started");

                    await TaskBodyImplAsync(source);

                    done();
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Exception");

                    throw; //Set this task to Faulted
                }
            });
        }

        protected abstract Task TaskBodyImplAsync(TSource source);
    }
    
    internal abstract class BlockingCollectionTaskBlockBase<TSource> : TaskBlockBase<BlockingCollection<TSource>>
    {
        protected BlockingCollectionTaskBlockBase(ILogger logger, BlockingCollection<TSource> source, Action done) : this(logger, source, 1, done)
        {
        }
        protected BlockingCollectionTaskBlockBase(ILogger logger, BlockingCollection<TSource> source, int maxDegreeOfParallelism, Action done) : base(logger, source, done)
        {
            this.partitioner = source.GetConsumingPartitioner();
            this.maxDegreeOfParallelism = maxDegreeOfParallelism;
        }

        private readonly Partitioner<TSource> partitioner;
        private readonly int maxDegreeOfParallelism;

        protected abstract Task ForEachBodyImplAsync(TSource item);

        protected override async Task TaskBodyImplAsync(BlockingCollection<TSource> source)
        {
            // UNTESTED ALTERNATIVE:
            //var ts = source.AsParallel().WithDegreeOfParallelism(maxDegreeOfParallelism).Select(i => ForEachBodyImplAsync(i));
            //await Task.WhenAll(ts);


            // FROM https://stackoverflow.com/a/14678329/1582323
            var partitions = partitioner.GetPartitions(maxDegreeOfParallelism);
            var tasks = partitions.Select(async (partition) =>
            {
                //await Task.Run(async () => 
                //{
                    using (partition)
                        while (partition.MoveNext())
                        //{
                        //    if (ct.IsCancellationRequested)
                        //        ct.ThrowIfCancellationRequested();

                            await ForEachBodyImplAsync(partition.Current);
                        //}
                //}, ct);
            });

            await Task.WhenAll(tasks);


            // PARALLEL.FOREACH does not actually await, it takes an Action<> (not a Func<Task, ...>) so it ends up being await async void
            //do
            //{
            //Parallel.ForEach(
            //        source,
            //        new ParallelOptions { MaxDegreeOfParallelism = maxDegreeOfParallelism },
            //        async (item) => await ForEachBodyImplAsync(item));
            //}
            //while (continueWhile());
        }
    }
}
