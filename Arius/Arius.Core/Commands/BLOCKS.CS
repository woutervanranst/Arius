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
        protected BlockBase(ILogger logger, /*Func<bool> continueWhile, */Action done)
        {
            this.logger = logger;
            //this.continueWhile = continueWhile;
            this.done = () =>
            {
                logger.LogTrace("Done");
                done();
            };
        }

        protected readonly ILogger logger;
        //protected readonly Func<bool> continueWhile;
        protected readonly Action done;

        public Task GetTask
        {
            get
            {
                var t = GetTaskImpl;
                tasks.Add(t);

                return t;
            }
        }

        protected abstract Task GetTaskImpl { get; }

        public static IEnumerable<Task> AllTasks => tasks.AsEnumerable();
        private static ConcurrentBag<Task> tasks = new();

    }


    internal abstract class SingleTaskBlockBase : BlockBase
    {
        protected SingleTaskBlockBase(ILogger logger, /*Func<bool> continueWhile, */Action done) : base(logger, /*continueWhile, */done)
        {
        }
        protected abstract Task TaskBodyImpl();
        protected override sealed Task GetTaskImpl
        {
            get
            {
                return Task.Run(() =>
                {
                    logger.LogTrace("Started");

                    TaskBodyImpl();

                    done();
                });
            }
        }
    }

    internal abstract class SingleThreadTaskBlockBase<TSource> : BlockBase
    {
        protected SingleThreadTaskBlockBase(ILogger logger, BlockingCollection<TSource> source, /*Func<bool> @while, */Action done) : base(logger, /*continueWhile, */done)
        {
            this.source = source;
        }
        private readonly BlockingCollection<TSource> source;

        protected abstract Task TaskBodyImplAsync(BlockingCollection<TSource> source);
        protected override sealed Task GetTaskImpl
        {
            get
            {
                return Task.Run(async () =>
                {
                    logger.LogTrace("Started");

                    await TaskBodyImplAsync(source);

                    done();
                });
            }
        }
    }






    //internal abstract class SingleThreadForEachTaskBlockBase<TSource> : BlockBase
    //{
    //    protected SingleThreadForEachTaskBlockBase(ILogger logger, IEnumerable<TSource> source, /*Func<bool> continueWhile, */Action done) : base(logger, /*continueWhile, */done)
    //    {
    //        this.source = source;
    //    }
    //    private readonly IEnumerable<TSource> source;


    //    protected abstract Task ForEachBodyImplAsync(TSource item);
    //    protected override sealed Task GetTaskImpl
    //    {
    //        get
    //        {
    //            return Task.Run(async () =>
    //            {
    //                logger.LogTrace("Started");

    //                foreach (var item in source)
    //                    await ForEachBodyImplAsync(item);

    //                done();
    //            });
    //        }
    //    }
    //}

    


    internal abstract class MultiThreadForEachTaskBlockBase<TSource> : BlockBase
    {
        protected MultiThreadForEachTaskBlockBase(ILogger logger, BlockingCollection<TSource> source, Action done) : this(logger, source, 1, done)
        {
        }
        protected MultiThreadForEachTaskBlockBase(ILogger logger, BlockingCollection<TSource> source, int maxDegreeOfParallelism, Action done) : base(logger, done)
        {
            this.partitioner = source.GetConsumingPartitioner();
            this.maxDegreeOfParallelism = maxDegreeOfParallelism;
        }

        private readonly Partitioner<TSource> partitioner;
        private readonly int maxDegreeOfParallelism;

        protected abstract Task ForEachBodyImplAsync(TSource item);

        protected override sealed Task GetTaskImpl
        {
            get
            {
                return Task.Run(async () =>
                {
                    logger.LogTrace("Started");

                    // UNTESTED ALTERNATIVE:
                    //var ts = source.AsParallel().WithDegreeOfParallelism(maxDegreeOfParallelism).Select(i => ForEachBodyImplAsync(i));
                    //await Task.WhenAll(ts);


                    // FROM https://stackoverflow.com/a/14678329/1582323
                    var partitions = partitioner.GetPartitions(maxDegreeOfParallelism);
                    var tasks = partitions.Select(async (partition) => 
                    {
                        using (partition)
                            while (partition.MoveNext())
                                await ForEachBodyImplAsync(partition.Current);
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

                    done();
                });
            }
        }
    }
}
