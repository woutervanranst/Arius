using System;
using System.Collections.Generic;
using System.Linq;
using Arius.Core.Facade;
using Azure.Storage;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;

namespace Arius.Core.Queries;

internal class StorageAccountQueries
{
    private readonly ILogger<StorageAccountQueries> logger;
    private readonly StorageAccountOptions          options;

    public StorageAccountQueries(ILogger<StorageAccountQueries> logger, StorageAccountOptions options)
    {
        this.logger  = logger;
        this.options = options;
    }

    public IAsyncEnumerable<string> GetContainerNamesAsync(int maxRetries)
    {
        var bco = new BlobClientOptions
        {
            Retry =
            {
                MaxRetries     = maxRetries,
                NetworkTimeout = TimeSpan.FromSeconds(5),
            }
        };

        var blobServiceClient = options.GetBlobServiceClient(bco);

        return blobServiceClient.GetBlobContainersAsync().Select(bci => bci.Name);
    }
}