using Arius.Core.Extensions;
using Arius.Core.Models;
using Arius.Core.Repositories;
using Arius.Core.Services;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Arius.Core.Commands.Restore;

internal class RestoreCommand : AsyncCommand<RestoreCommandOptions>
{
    public RestoreCommand(ILoggerFactory loggerFactory, Repository repo)
    {
        this.loggerFactory = loggerFactory;
        this.repo          = repo;
        this.logger        = loggerFactory.CreateLogger<RestoreCommand>();
    }

    private readonly ILoggerFactory          loggerFactory;
    private readonly Repository              repo;
    private readonly ILogger<RestoreCommand> logger;

    public async Task<CommandResultStatus> ExecuteImplAsync(RestoreCommandOptions options)
    {
        var hashValueProvider = new SHA256Hasher(options);
        var fileService       = new FileService(loggerFactory.CreateLogger<FileService>(), hashValueProvider);
        var fileSystemService = new FileSystemService(loggerFactory.CreateLogger<FileSystemService>());


        var binariesToDownload = Channel.CreateUnbounded<PointerFile>();

        var provisionPointerFilesBlock = new ProvisionPointerFilesBlock(
            loggerFactory: loggerFactory,
            sourceFunc: () => options.Path, //S10
            maxDegreeOfParallelism: options.IndexBlock_Parallelism,
            options: options,
            repo: repo,
            fileSystemService: fileSystemService,
            fileService: fileService,
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
        var indexTask = provisionPointerFilesBlock.GetTask;


        var chunkRehydrating = false;

        var downloadBinaryBlock = new DownloadBinaryBlock(
            loggerFactory: loggerFactory,
            sourceFunc: () => binariesToDownload,
            maxDegreeOfParallelism: options.DownloadBinaryBlock_Parallelism,
            fileService: fileService,
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
            await repo.DeleteHydratedChunksFolderAsync();
        }
        else
        {
            logger.LogWarning("WARNING: Not all files are restored as chunks are still being hydrated. Please run the restore operation again in 12-24 hours.");
        }

        if (BlockBase.AllTasks.Where(t => t.Status == TaskStatus.Faulted) is var ts && ts.Any())
        {
            var exceptions = ts.Select(t => t.Exception);
            foreach (var e in exceptions)
                logger.LogError(e);

            throw new AggregateException(exceptions);
        }

        return CommandResultStatus.Success;
    }
}