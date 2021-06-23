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
            this.done = done;
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
        protected abstract void TaskBodyImpl();
        protected override sealed Task GetTaskImpl
        {
            get
            {
                return Task.Run(() =>
                {
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
        protected abstract void ForEachBodyImpl(TSource item);

        protected override sealed Task GetTaskImpl
        {
            get
            {
                return Task.Run(() =>
                {
                    //do
                    //{
                        Parallel.ForEach(
                            source,
                            new ParallelOptions { MaxDegreeOfParallelism = maxDegreeOfParallelism },
                            item => ForEachBodyImpl(item));
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

            mre.Set();
        }

        private AsyncManualResetEvent mre = new();

        public Task WaitAddingCompleted => mre.WaitAsync();
    }
}
