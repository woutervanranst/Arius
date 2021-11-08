using Arius.Core.Commands.Archive;
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
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Arius.Core.Commands;

internal abstract class BlockBase
{
    public static IEnumerable<Task> AllTasks => tasks.AsEnumerable(); //Put this on the nongeneric base class to have access to it via BlockBase.AllTasks. Cannot access static members on generic classes.
    protected static readonly ConcurrentBag<Task> tasks = new();

    internal static void Reset()
    {
        //for unit testing purposes as the static survives the lifetime of the unit test
        tasks.Clear(); 
        cts = new();
    }

    protected static CancellationTokenSource cts = new(); //put this here since static fields are not shared in generic classes with different T
    private static CancellationToken ct => cts.Token;
    public static Task CancellationTask => new CancellationTokenTaskSource<object>(ct).Task;
}

internal abstract class TaskBlockBase<TSource> : BlockBase
{
    protected TaskBlockBase(ILoggerFactory loggerFactory, Func<TSource> sourceFunc, Action done = default)
        : this(loggerFactory, () => Task.FromResult(sourceFunc()), done)
    {
    }
    protected TaskBlockBase(ILoggerFactory loggerFactory, Func<Task<TSource>> sourceFunc, Action done = default)
    {
        this.logger = loggerFactory.CreateLogger(this.GetType());
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
    
//internal abstract class BlockingCollectionTaskBlockBase<TSource> : TaskBlockBase<BlockingCollection<TSource>>
//{
//    protected BlockingCollectionTaskBlockBase(ILoggerFactory loggerFactory, Func<BlockingCollection<TSource>> sourceFunc, int degreeOfParallelism = 1, Action done = default)
//        : this(loggerFactory, () => Task.FromResult(sourceFunc()), degreeOfParallelism, done)
//    {
//    }
//    protected BlockingCollectionTaskBlockBase(ILoggerFactory loggerFactory, Func<Task<BlockingCollection<TSource>>> sourceFunc, int degreeOfParallelism = 1, Action done = default) 
//        : base(loggerFactory, sourceFunc, done)
//    {
//        this.degreeOfParallelism = degreeOfParallelism;
//    }

//    private readonly int degreeOfParallelism;

//    protected override async Task TaskBodyImplAsync(BlockingCollection<TSource> source)
//    {
//        //TODO quid CancellationToken?
//        await source.AsyncParallelForEachAsync(ForEachBodyImplAsync, degreeOfParallelism);
//    }

//    protected abstract Task ForEachBodyImplAsync(TSource item);

//}

internal abstract class ChannelTaskBlockBase<TSource> : TaskBlockBase<ChannelReader<TSource>>
{
    protected ChannelTaskBlockBase(ILoggerFactory loggerFactory, Func<ChannelReader<TSource>> sourceFunc, int maxDegreeOfParallelism = 1, Action done = default)
        : this(loggerFactory, () => Task.FromResult(sourceFunc()), maxDegreeOfParallelism, done)
    {
    }
    protected ChannelTaskBlockBase(ILoggerFactory loggerFactory, Func<Task<ChannelReader<TSource>>> sourceFunc, int maxDegreeOfParallelism = 1, Action done = default)
        : base(loggerFactory, sourceFunc, done)
    {
        this.maxDegreeOfParallelism = maxDegreeOfParallelism;
    }

    private readonly int maxDegreeOfParallelism;

    protected override async Task TaskBodyImplAsync(ChannelReader<TSource> source)
    {
        //TODO quid CancellationToken? -- for errors it s OK as it is, perhaps if other tasks are cancelled?
        await Parallel.ForEachAsync(source.ReadAllAsync(),
            new ParallelOptions { MaxDegreeOfParallelism = maxDegreeOfParallelism },
            async (item, cancellationToken) => await ForEachBodyImplAsync(item, cancellationToken));
    }

    protected abstract Task ForEachBodyImplAsync(TSource item, CancellationToken cancellationToken);
}