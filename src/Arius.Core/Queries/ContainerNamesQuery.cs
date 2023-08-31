using Arius.Core.Facade;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Arius.Core.Queries;

internal class QueryContainerNamesOptions : IQueryOptions
{
    public required int MaxRetries { get; init; }

    public void Validate()
    {
        // Always succeeds
    }
}

internal class QueryContainerNamesResult : IQueryResult
{
    public required QueryResultStatus        Status         { get; init; }
    public required IAsyncEnumerable<string> ContainerNames { get; init; }
}

internal class ContainerNamesQuery : IQuery<QueryContainerNamesOptions, QueryContainerNamesResult>
{
    private readonly ILogger<ContainerNamesQuery> logger;
    private readonly StorageAccountOptions        storageAccountOptions;

    public ContainerNamesQuery(ILogger<ContainerNamesQuery> logger, StorageAccountOptions options)
    {
        this.logger                = logger;
        this.storageAccountOptions = options;
    }

    public QueryContainerNamesResult Execute(QueryContainerNamesOptions queryOptions)
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

        return new QueryContainerNamesResult
        {
            Status = QueryResultStatus.Success, 
            ContainerNames = blobServiceClient.GetBlobContainersAsync().Select(bci => bci.Name)
        };
    }
}