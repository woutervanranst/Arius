using Arius.Core.Shared.Storage;
using FluentValidation;
using Mediator;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;

namespace Arius.Core.Features.Queries.ContainerNames;

public sealed record ContainerNamesQuery : StorageAccountCommandProperties, IStreamQuery<string>
{
}

internal class ContainerNamesQueryHandler : IStreamQueryHandler<ContainerNamesQuery, string>
{
    private readonly ILoggerFactory loggerFactory;

    public ContainerNamesQueryHandler(ILoggerFactory loggerFactory)
    {
        this.loggerFactory = loggerFactory;
    }
    public async IAsyncEnumerable<string> Handle(ContainerNamesQuery request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        //var storageAccount = _storageAccountFactory.GetStorageAccount(request.StorageAccountName);
        //var storage        = storageAccount.GetRepositoryStorage(request.RepositoryName);
        //await foreach (var name in storage.GetNamesAsync(string.Empty, cancellationToken).WithCancellation(cancellationToken))
        //{
        //    yield return name;
        //}

        await new StorageAccountValidator().ValidateAndThrowAsync(request, cancellationToken);

        var storage = new AzureBlobStorageAccount(request.AccountName, request.AccountKey, false, loggerFactory.CreateLogger<AzureBlobStorageAccount>());

        await foreach (var containerName in storage.GetContainerNames().WithCancellation(cancellationToken))
        {
            yield return containerName;
        }
    }
}
