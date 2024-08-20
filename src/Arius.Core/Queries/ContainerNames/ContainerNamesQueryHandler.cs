using Arius.Core.Facade;
using System.Collections.Generic;

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
        var blobServiceClient = storageAccountOptions.GetBlobServiceClient(queryOptions.MaxRetries, TimeSpan.FromSeconds(5));

        return (QueryResultStatus.Success, blobServiceClient.GetBlobContainersAsync().Select(bci => bci.Name));
    }
}