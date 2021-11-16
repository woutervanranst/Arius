using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Channels;
using System.Threading.Tasks;
using Arius.Core.Configuration;
using Arius.Core.Extensions;
using Arius.Core.Models;
using Arius.Core.Repositories;
using Arius.Core.Services;
using Arius.Core.Services.Chunkers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Arius.Core.Commands.Restore;

internal class RestoreCommand : ICommand //This class is internal but the interface is public for use in the Facade
{
    public RestoreCommand(RestoreCommandOptions options,
        ILogger<RestoreCommand> logger,
        IServiceProvider serviceProvider)
    {
        this.options = options;
        this.logger = logger;
        services = serviceProvider;
    }

    private readonly RestoreCommandOptions options;
    private readonly ILogger<RestoreCommand> logger;
    private readonly IServiceProvider services;

    IServiceProvider ICommand.Services => services;

    public async Task<int> Execute()
    {
        var loggerFactory = services.GetRequiredService<ILoggerFactory>();
        var repo = services.GetRequiredService<Repository>();
        var pointerService = services.GetRequiredService<PointerService>();

        var binariesToDownload = Channel.CreateUnbounded<PointerFile>();

        var indexBlock = new IndexBlock(
            loggerFactory: loggerFactory,
            sourceFunc: () => options.Path, //S10
            maxDegreeOfParallelism: options.IndexBlock_Parallelism,
            synchronize: options.Synchronize,
            repo: repo,
            pointerService: pointerService,
            onIndexedPointerFile: async arg =>
            {
                if (!options.Download)
                    return; //no need to download

                await binariesToDownload.Writer.WriteAsync(arg);
            },
            onCompleted: () => 
            {
                binariesToDownload.Writer.Complete(); //S13
            });
        var indexTask = indexBlock.GetTask;

        var chunkRehydrating = false;

        var downloadBinaryBlock = new DownloadBinaryBlock(
            loggerFactory: loggerFactory,
            sourceFunc: () => binariesToDownload,
            maxDegreeOfParallelism: options.DownloadBinaryBlock_Parallelism,
            pointerService: pointerService,
            options: options,
            repo: repo,
            chunkRehydrating: () =>
            {
                chunkRehydrating = true;
            },
            onCompleted: () => 
            {
            });
        var downloadBinaryTask = downloadBinaryBlock.GetTask;

        await Task.WhenAny(Task.WhenAll(BlockBase.AllTasks), BlockBase.CancellationTask);

        if (!chunkRehydrating)
        {
            logger.LogInformation("All binaries restored, deleting temporary hydration folder, if applicable");
            await repo.Chunks.DeleteHydrateFolderAsync();
        }

        if (BlockBase.AllTasks.Where(t => t.Status == TaskStatus.Faulted) is var ts
            && ts.Any())
        {
            var exceptions = ts.Select(t => t.Exception);
            throw new AggregateException(exceptions);
        }

        return 0;
    }
}