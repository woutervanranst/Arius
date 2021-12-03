using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace Arius.Core.Commands.Rehydrate;


public interface IRehydrateCommandOptions : IRepositoryOptions // the interface is public, the implementation internal
{
}

internal class RehydrateCommand : ICommand<IRehydrateCommandOptions>
{
    private IServiceProvider services;

    IServiceProvider ICommand<IRehydrateCommandOptions>.Services => services;

    public async Task<int> ExecuteAsync(IRehydrateCommandOptions options)
    {
        var connectionString = $"DefaultEndpointsProtocol=https;AccountName={options.AccountName};AccountKey={options.AccountKey};EndpointSuffix=core.windows.net";
        var container = new BlobContainerClient(connectionString, blobContainerName: options.Container);

        var archivedBlobs = container.GetBlobsAsync(prefix: "chunks").Where(bi => bi.Properties.AccessTier == AccessTier.Archive);

        var count = 0;
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