using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Arius.Core.Commands;
using Arius.Core.Commands.Archive;
using Arius.Core.Services.Chunkers;
using Azure;
using Azure.Core;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;

namespace Arius.Core.Repositories;

internal partial class Repository
{
    public Repository(ILoggerFactory loggerFactory, IRepositoryOptions options, Chunker chunker)
    {
        var logger = loggerFactory.CreateLogger<Repository>();
        var connectionString = $"DefaultEndpointsProtocol=https;AccountName={options.AccountName};AccountKey={options.AccountKey};EndpointSuffix=core.windows.net";
        var container = new BlobContainerClient(
            connectionString, 
            blobContainerName: options.Container,
            /* 
             * RequestFailedException: The condition specified using HTTP conditional header(s) is not met.
             *      -- this is a throttling error most likely, hence specifiying exponential backoff
             *      as per https://docs.microsoft.com/en-us/azure/architecture/best-practices/retry-service-specific#blobs-queues-and-files
             */
            options: new BlobClientOptions()
            {
                Retry =
                {
                    Delay = TimeSpan.FromSeconds(2),        //The delay between retry attempts for a fixed approach or the delay on which to base calculations for a backoff-based approach
                    MaxRetries = 5,                         //The maximum number of retry attempts before giving up
                    Mode = RetryMode.Exponential,           //The approach to use for calculating retry delays
                    MaxDelay = TimeSpan.FromSeconds(60)     //The maximum permissible delay between retry attempts
                }
            });

        var r0 = container.CreateIfNotExists(PublicAccessType.None);
        if (r0 is not null && r0.GetRawResponse().Status == (int)HttpStatusCode.Created)
            logger.LogInformation($"Created container {options.Container}... ");

        Binaries = new(loggerFactory.CreateLogger<BinaryRepository>(), this, container, chunker);
        Chunks = new(loggerFactory.CreateLogger<ChunkRepository>(), this, container, options.Passphrase);
        PointerFileEntries = new(loggerFactory.CreateLogger<PointerFileEntryRepository>(), this);
        States = new(loggerFactory.CreateLogger<StateRepository>(), this, container, options.Passphrase);
    }

    private static readonly BlockBlobOpenWriteOptions ThrowOnExistOptions = new() // as per https://github.com/Azure/azure-sdk-for-net/issues/24831#issue-1031369473
    {
        OpenConditions = new BlobRequestConditions { IfNoneMatch = new ETag("*") }
    };

    public async Task<(int binaryCount, long binariesSize, int pointerFileEntryCount)> GetCurrentStats()
    {
        var binaryCount = await Binaries.CountAsync();
        var binariesSize = await Binaries.TotalIncrementalLengthAsync();
        var pfes = await PointerFileEntries.GetCurrentEntriesAsync(false);
        pfes.TryGetNonEnumeratedCount(out var pointerFileEntryCount);

        return (binaryCount, binariesSize, pointerFileEntryCount);
    }
}