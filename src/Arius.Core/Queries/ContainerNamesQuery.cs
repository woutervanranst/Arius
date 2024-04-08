using System;
using System.Collections.Generic;
using System.Linq;
using Arius.Core.Facade;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;

namespace Arius.Core.Queries;

internal record QueryContainerNamesOptions : QueryOptions
{
    public required int MaxRetries { get; init; }

    public override void Validate()
    {
        // Always succeeds
    }
}


internal class ContainerNamesQuery : Query<QueryContainerNamesOptions, IAsyncEnumerable<string>>
{
    private readonly ILogger<ContainerNamesQuery> logger;
    private readonly StorageAccountOptions        storageAccountOptions;

    public ContainerNamesQuery(ILogger<ContainerNamesQuery> logger, StorageAccountOptions options)
    {
        this.logger                = logger;
        this.storageAccountOptions = options;
    }

    protected override (QueryResultStatus Status, IAsyncEnumerable<string>? Result) ExecuteImpl(QueryContainerNamesOptions queryOptions)
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