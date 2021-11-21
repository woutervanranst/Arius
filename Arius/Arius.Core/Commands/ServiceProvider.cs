using System;
using Arius.Core.Repositories;
using Arius.Core.Services;
using Arius.Core.Services.Chunkers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Arius.Core.Commands;

internal abstract class ServiceProvider
{
    protected void InitServiceProvider(ILoggerFactory loggerFactory, IRepositoryOptions options)
    {
        services = new ServiceCollection()
            .AddSingleton<ILoggerFactory>(loggerFactory)
            .AddLogging()

            .AddSingleton<PointerService>()
            .AddSingleton<IHashValueProvider, SHA256Hasher>()
            .AddSingleton<Repository>()

            .AddSingleton<Chunker, ByteBoundaryChunker>()

            .AddSingleton<IRepositoryOptions>(options)

            .BuildServiceProvider();
    }

    private IServiceProvider services;

    protected IServiceProvider Services => services ?? throw new InvalidOperationException($"{nameof(ServiceProvider)} not yet initialized");
}