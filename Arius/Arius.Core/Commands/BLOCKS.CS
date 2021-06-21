using Arius.Core.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Arius.Core.Commands
{
    internal abstract class BlockBase
    {
        protected BlockBase(Func<bool> continueWhile, Action done)
        {
            this.continueWhile = continueWhile;
            this.done = done;
        }
        protected readonly Func<bool> continueWhile;
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
        protected SingleTaskBlockBase(Func<bool> continueWhile, Action done) : base(continueWhile, done)
        {
        }
        protected abstract void TaskBodyImpl();
        protected override sealed Task GetTaskImpl
        {
            get
            {
                return Task.Run(() =>
                {
                    do
                    {
                        TaskBodyImpl();
                    }
                    while (continueWhile());

                    done();
                });
            }
        }
    }

    internal abstract class SingleThreadForEachTaskBlockBase<TSource> : BlockBase
    {
        protected SingleThreadForEachTaskBlockBase(IEnumerable<TSource> source, Func<bool> continueWhile, Action done) : base(continueWhile, done)
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
                    do
                    {
                        foreach (var item in source)
                        {
                            await ForEachBodyImplAsync(item);
                        }
                    }
                    while (continueWhile());

                    done();
                });
            }
        }
    }


    internal abstract class MultiThreadForEachTaskBlockBase<TSource> : BlockBase
    {
        protected MultiThreadForEachTaskBlockBase(Func<bool> continueWhile, Action done) : base(continueWhile, done)
        {
        }

        protected abstract Partitioner<TSource> Source { get; }
        protected abstract int MaxDegreeOfParallelism { get; }
        protected abstract void ForEachBodyImpl(TSource item);

        protected override sealed Task GetTaskImpl
        {
            get
            {
                return Task.Run(() =>
                {
                    do
                    {
                        Parallel.ForEach(
                            Source,
                            new ParallelOptions { MaxDegreeOfParallelism = MaxDegreeOfParallelism },
                            item => ForEachBodyImpl(item));
                    }
                    while (continueWhile());

                    done();
                });
            }
        }
    }
}
