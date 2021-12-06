﻿using System;
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
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Arius.Core.Commands.Restore;

internal class RestoreCommand : ICommand<IRestoreCommandOptions> //This class is internal but the interface is public for use in the Facade
{
    public RestoreCommand(ILoggerFactory loggerFactory, ILogger<RestoreCommand> logger)
    {
        this.loggerFactory = loggerFactory;
        this.logger = logger;
    }

    private readonly ILoggerFactory loggerFactory;
    private readonly ILogger<RestoreCommand> logger;

    private ExecutionServiceProvider<IRestoreCommandOptions> executionServices;

    IServiceProvider ICommand<IRestoreCommandOptions>.Services => executionServices.Services;

    public async Task<int> ExecuteAsync(IRestoreCommandOptions options)
    {
        var validator = new IRestoreCommandOptions.Validator();
        await validator.ValidateAndThrowAsync(options);

        executionServices = ExecutionServiceProvider<IRestoreCommandOptions>.BuildServiceProvider(loggerFactory, options);
        var repo = executionServices.GetRequiredService<Repository>();
        var pointerService = executionServices.GetRequiredService<PointerService>();


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
        else
        {
            logger.LogWarning("WARNING: Not all files are restored as chunks are still being hydrated. Please run the restore operation again in 12-24 hours.");
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