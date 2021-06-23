using Arius.Core.Models;
using Microsoft.Extensions.Logging;
using Nito.AsyncEx;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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

                    //do
                    //{
                    TaskBodyImpl();
                    //}
                    //while (continueWhile());

                    done();
                });
            }
        }
    }

    internal abstract class SingleThreadForEachTaskBlockBase<TSource> : BlockBase
    {
        protected SingleThreadForEachTaskBlockBase(ILogger logger, IEnumerable<TSource> source, /*Func<bool> continueWhile, */Action done) : base(logger, /*continueWhile, */done)
        {
            this.source = source;
        }
        private readonly IEnumerable<TSource> source;


        protected abstract Task ForEachBodyImplAsync(TSource item);
        protected override sealed Task GetTaskImpl
        {
            get
            {
                return Task.Run(async () =>
                {
                    logger.LogTrace("Started");

                    //do
                    //{
                    foreach (var item in source)
                    {
                        await ForEachBodyImplAsync(item);
                    }
                    //}
                    //while (continueWhile());

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
            //this.@while = @while;
        }
        private readonly BlockingCollection<TSource> source;
        //private readonly Func<bool> @while;

        protected abstract Task TaskBodyImplAsync(BlockingCollection<TSource> source);
        protected override sealed Task GetTaskImpl
        {
            get
            {
                return Task.Run(async () =>
                {
                    logger.LogTrace("Started");

                    //while (@while())
                    //{
                    //    foreach (var item in source)
                    //    {
                    await TaskBodyImplAsync(source);
                    //    }
                    //}

                    done();
                });
            }
        }
    }


    internal abstract class MultiThreadForEachTaskBlockBase<TSource> : BlockBase
    {
        protected MultiThreadForEachTaskBlockBase(ILogger logger, /*Func<bool> continueWhile, */Partitioner<TSource> source, int maxDegreeOfParallelism, Action done) : base(logger, /*continueWhile, */done)
        {
            this.source = source;
            this.maxDegreeOfParallelism = maxDegreeOfParallelism;
        }

        private readonly Partitioner<TSource> source;
        private readonly int maxDegreeOfParallelism;

        //protected abstract Partitioner<TSource> Source { get; }
        //protected abstract int MaxDegreeOfParallelism { get; }
        protected abstract Task ForEachBodyImplAsync(TSource item);

        protected override sealed Task GetTaskImpl
        {
            get
            {
                return Task.Run(() =>
                {
                    logger.LogTrace("Started");

                    //do
                    //{
                    Parallel.ForEach(
                            source,
                            new ParallelOptions { MaxDegreeOfParallelism = maxDegreeOfParallelism },
                            async (item) => await ForEachBodyImplAsync(item));
                    //}
                    //while (continueWhile());

                    done();
                });
            }
        }
    }



    public class BlockingCollectionEx<T> : BlockingCollection<T>
    {
        public new void CompleteAdding()
        {
            base.CompleteAdding();

            completeAddingManualResetEvent.Set();
        }

        public Task WaitAddingCompleted => completeAddingManualResetEvent.WaitAsync();
        private readonly AsyncManualResetEvent completeAddingManualResetEvent = new();


        public new IEnumerable<T> GetConsumingEnumerable(CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }
        public new IEnumerable<T> GetConsumingEnumerable()
        {
            foreach (var item in base.GetConsumingEnumerable())
                yield return item;

            completedManualResetEvent.SetResult();
        }

        //public Task WaitCompleted => completedManualResetEvent.WaitAsync();
        //private readonly AsyncManualResetEvent completedManualResetEvent = new();
        public Task WaitCompleted => completedManualResetEvent.Task;
        private readonly TaskCompletionSource completedManualResetEvent = new();

        public Partitioner<T> GetConsumingPartitioner()
        {
            throw new NotImplementedException();
        }

        /*
            * 
            * TO TEST
    var x = new BlockingCollectionEx<int> { 1, 2, 3 };
    x.CompleteAdding();
    Task.Run(async () =>
    {
        await Task.Delay(1000);
        foreach (var item in x.GetConsumingEnumerable())
        {
        }
    });

    await x.WaitCompleted;

            */
    }
}
