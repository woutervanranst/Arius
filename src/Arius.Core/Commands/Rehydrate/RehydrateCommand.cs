﻿using Arius.Core.Facade;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Arius.Core.Commands.Rehydrate;

internal class RehydrateCommand : AsyncCommand<RehydrateCommandOptions>
{
    public RehydrateCommand(ILogger<RehydrateCommand> logger)
    {
        this.logger = logger;
    }

    private readonly ILogger<RehydrateCommand> logger;

    protected override async Task<CommandResultStatus> ExecuteImplAsync(RehydrateCommandOptions options)
    {
        var container = options.GetBlobContainerClient();

        var archivedBlobs = await container.GetBlobsAsync(prefix: "chunks")
            .Where(bi => bi.Properties.AccessTier == AccessTier.Archive &&
                         bi.Properties.ArchiveStatus == null) //not RehydratePendingToCool
            .ToArrayAsync();

        var size  = archivedBlobs.Sum(bi => bi.Properties.ContentLength);
        var count = archivedBlobs.Count();

        logger.LogInformation("Estimated price as per https://azure.microsoft.com/en-us/pricing/details/storage/blobs/ (North Europe -- Read Operations, All other Operations)");
        logger.LogInformation($"SetAccessTier Operation: {count} blobs * 6.3860 EUR/10k operations = {count * 6.3860 / 10_000} EUR");
        logger.LogInformation($"Data Retrieval: {(size / 1024 / 1024 / 1024)} GB * 0.0197 EUR/GB = {size / 1024 / 1024 / 1024 * 0.0197} EUR");

        count = 0;
        const int MAX_COUNT = 10_000;
        var cts = new CancellationTokenSource();

        try
        {
            await Parallel.ForEachAsync(archivedBlobs, cts.Token, async (bi, ct) =>
            {
                if (count > MAX_COUNT)
                    cts.Cancel();


                var bc = container.GetBlobClient(bi.Name);
                await bc.SetAccessTierAsync(AccessTier.Cool);

                Interlocked.Add(ref count, 1);
            });
        }
        catch (TaskCanceledException e)
        {
            // we did not rehydrate enough
            if (count < MAX_COUNT)
                throw e;
        }
        
        return 0;
    }
}