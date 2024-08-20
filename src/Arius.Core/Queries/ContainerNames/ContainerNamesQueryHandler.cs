using System;
using System.Collections.Generic;
using System.Linq;
using Arius.Core.Facade;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;

namespace Arius.Core.Queries.ContainerNames;

internal class ContainerNamesQueryHandler : Query<ContainerNamesQuery, IAsyncEnumerable<string>>
{
    private readonly ILogger<ContainerNamesQueryHandler> logger;
    private readonly StorageAccountOptions               storageAccountOptions;

    public ContainerNamesQueryHandler(ILogger<ContainerNamesQueryHandler> logger, StorageAccountOptions options)
    {
        this.logger = logger;
        storageAccountOptions = options;
    }

    protected override (QueryResultStatus Status, IAsyncEnumerable<string>? Result) ExecuteImpl(ContainerNamesQuery queryOptions)
    {
        var bco = new BlobClientOptions
        {
            Retry =
                {
                    MaxRetries     = queryOptions.MaxRetries,
                    NetworkTimeout = TimeSpan.FromSeconds(5),
                }
        };

        var blobServiceClient = storageAccountOptions.GetBlobServiceClient(bco);

        return (QueryResultStatus.Success, blobServiceClient.GetBlobContainersAsync().Select(bci => bci.Name));
    }
}