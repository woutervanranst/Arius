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
        protected abstract void BodyImpl();
        protected override sealed Task GetTaskImpl
        {
            get
            {
                return Task.Run(() =>
                {
                    do
                    {
                        BodyImpl();
                    }
                    while (continueWhile());

                    done();
                });
            }
        }
    }

    internal abstract class MultiTaskBlockBase<TSource> : BlockBase
    {
        protected MultiTaskBlockBase(Func<bool> continueWhile, Action done) : base(continueWhile, done)
        {
        }

        protected abstract Partitioner<TSource> Source { get; }
        protected abstract int MaxDegreeOfParallelism { get; }
        protected abstract void BodyImpl(TSource item);

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
                            item => BodyImpl(item));
                    }
                    while (continueWhile());

                    done();
                });
            }
        }
    }
}
