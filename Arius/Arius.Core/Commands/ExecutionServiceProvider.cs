using System;
using Arius.Core.Repositories;
using Arius.Core.Services;
using Arius.Core.Services.Chunkers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Arius.Core.Commands;

internal class ExecutionServiceProvider<TOptions> where TOptions : IRepositoryOptions
{
    public static ExecutionServiceProvider<TOptions> BuildServiceProvider(ILoggerFactory loggerFactory, TOptions options)
    {
        return new ExecutionServiceProvider<TOptions>()
        {
            Options = options,

            services = new ServiceCollection()
                .AddSingleton<ILoggerFactory>(loggerFactory)
                .AddLogging()

                .AddSingleton<PointerService>()
                .AddSingleton<IHashValueProvider, SHA256Hasher>()
                .AddSingleton<Repository>()

                .AddSingleton<Chunker, ByteBoundaryChunker>()

                .AddSingleton<IRepositoryOptions>(options)

                .BuildServiceProvider()
        };
    }

    public TOptions Options { get; init; }

    public IServiceProvider Services => services; // ?? throw new InvalidOperationException($"{nameof(ServiceProvider)} not yet initialized");
    private IServiceProvider services;
    //public IServiceProvider Services { get; init; }

    public T GetRequiredService<T>() => services.GetRequiredService<T>();

}