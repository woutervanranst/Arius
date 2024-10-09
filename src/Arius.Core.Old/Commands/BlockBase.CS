using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Arius.Core.Extensions;
using Microsoft.Extensions.Logging;
using Nito.AsyncEx;

namespace Arius.Core.Commands;

internal abstract class BlockBase
{
    public static IEnumerable<Task> AllTasks => tasks.AsEnumerable(); //Put this on the nongeneric base class to have access to it via BlockBase.AllTasks. Cannot access static members on generic classes.
    protected static readonly ConcurrentBag<Task> tasks = [];

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


internal abstract class TaskBlockBase : BlockBase
{
    protected TaskBlockBase(ILoggerFactory loggerFactory, Action? onCompleted = default)
    {
        this.logger = loggerFactory.CreateLogger(this.GetType());

        this.onCompleted = () =>
        {
            onCompleted?.Invoke();
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

    protected readonly ILogger logger;
    private readonly Lazy<Task> task;
    protected readonly Action onCompleted;

    public Task GetTask => task.Value;

    protected virtual Task TaskImpl()
    {
        return Task.Run(async () =>
        {
            try
            {
                logger.LogTrace("Started");

                await TaskBodyImplAsync();

                logger.LogDebug($"Completed {this.GetType().Name}");
                onCompleted();
            }
            catch (Exception e)
            {
                logger.LogError(e);

                throw; //Set this task to Faulted
            }
        });
    }

    protected abstract Task TaskBodyImplAsync();
}


internal abstract class TaskBlockBase<TSource> : TaskBlockBase
{
    protected TaskBlockBase(ILoggerFactory loggerFactory, Func<TSource> sourceFunc, Action? onCompleted = default)
        : this(loggerFactory, () => Task.FromResult(sourceFunc()), onCompleted)
    {
    }
    protected TaskBlockBase(ILoggerFactory loggerFactory, Func<Task<TSource>> sourceFunc, Action? onCompleted = default)
        : base(loggerFactory, onCompleted)
    {
        this.sourceFunc = sourceFunc;
    }

    private readonly Func<Task<TSource>> sourceFunc;

    protected override Task TaskImpl()
    {
        return Task.Run(async () =>
        {
            try
            {
                logger.LogTrace("Started");

                var source = await sourceFunc();
                await TaskBodyImplAsync(source);

                logger.LogDebug($"Completed {this.GetType().Name}");
                onCompleted();
            }
            catch (Exception e)
            {
                logger.LogError(e);

                throw; //Set this task to Faulted
            }
        });
    }

    protected sealed override Task TaskBodyImplAsync() => throw new NotImplementedException();
    protected abstract Task TaskBodyImplAsync(TSource source);
}

    
internal abstract class ChannelTaskBlockBase<TSource> : TaskBlockBase<ChannelReader<TSource>>
{
    protected ChannelTaskBlockBase(ILoggerFactory loggerFactory, Func<ChannelReader<TSource>> sourceFunc, int maxDegreeOfParallelism = 1, Action? onCompleted = default)
        : this(loggerFactory, () => Task.FromResult(sourceFunc()), maxDegreeOfParallelism, onCompleted)
    {
    }
    protected ChannelTaskBlockBase(ILoggerFactory loggerFactory, Func<Task<ChannelReader<TSource>>> sourceFunc, int maxDegreeOfParallelism = 1, Action? onCompleted = default)
        : base(loggerFactory, sourceFunc, onCompleted)
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