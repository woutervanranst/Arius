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
        public static IEnumerable<Task> AllTasks => tasks.AsEnumerable(); //Put this on the nongeneric base class to have access to it via BlockBase.AllTasks. Cannot access static members on generic classes.
        protected static readonly ConcurrentBag<Task> tasks = new();

        protected static readonly CancellationTokenSource cts = new(); //put this here since static fields are not shared in generic classes with different T
        private static readonly CancellationToken ct = cts.Token;
        public static Task CancellationTask => new CancellationTokenTaskSource<object>(ct).Task;
    }

    internal abstract class TaskBlockBase<TSource> : BlockBase
    {
        protected TaskBlockBase(ILogger logger, Func<TSource> sourceFunc, Action done = default)
            : this(logger, () => Task.FromResult(sourceFunc()), done)
        {
        }
        protected TaskBlockBase(ILogger logger, Func<Task<TSource>> sourceFunc, Action done = default)
        {
            this.logger = logger;
            this.sourceFunc = sourceFunc;

            this.done = () =>
            {
                done?.Invoke();
                logger.LogTrace("Done");
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

        private readonly Func<Task<TSource>> sourceFunc;
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

                    var source = await sourceFunc();
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
        protected BlockingCollectionTaskBlockBase(ILogger logger, Func<BlockingCollection<TSource>> sourceFunc, int maxDegreeOfParallelism = 1, Action done = default)
            : this(logger, () => Task.FromResult(sourceFunc()), maxDegreeOfParallelism, done)
        {
        }
        protected BlockingCollectionTaskBlockBase(ILogger logger, Func<Task<BlockingCollection<TSource>>> sourceFunc, int maxDegreeOfParallelism = 1, Action done = default) 
            : base(logger, sourceFunc, done)
        {
            this.maxDegreeOfParallelism = maxDegreeOfParallelism;
        }

        private readonly int maxDegreeOfParallelism;

        protected abstract Task ForEachBodyImplAsync(TSource item);

        protected override async Task TaskBodyImplAsync(BlockingCollection<TSource> source)
        {
            // FROM https://stackoverflow.com/a/14678329/1582323
            var partitions = source.GetConsumingPartitioner().GetPartitions(maxDegreeOfParallelism);
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

            // UNTESTED ALTERNATIVE:
            //var ts = source.AsParallel().WithDegreeOfParallelism(maxDegreeOfParallelism).Select(i => ForEachBodyImplAsync(i));
            //await Task.WhenAll(ts);


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
