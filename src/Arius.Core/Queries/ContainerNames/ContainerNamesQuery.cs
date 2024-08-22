using Arius.Core.Domain.Storage;
using FluentValidation;
using MediatR;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Arius.Core.Queries.ContainerNames;

public record ContainerNamesQuery : IRequest<IAsyncEnumerable<string>>
{
    public ContainerNamesQuery(string accountName, string accountKey)
    {
        AccountName = accountName;
        AccountKey  = accountKey;
    }

    public ContainerNamesQuery(StorageAccountOptions storageAccountOptions)
    {
        AccountName = storageAccountOptions.AccountName;
        AccountKey  = storageAccountOptions.AccountKey;
    }
    public ContainerNamesQuery(IStorageAccount storageAccount)
    {
        AccountName = storageAccount.AccountName;
        AccountKey  = storageAccount.AccountKey;
    }

    public string AccountName { get; }
    public string AccountKey  { get; }
}


internal class ContainerNamesQueryValidator : AbstractValidator<ContainerNamesQuery>
{
    public ContainerNamesQueryValidator()
    {
        RuleFor(query => new StorageAccountOptions { AccountName = query.AccountName, AccountKey = query.AccountKey })
            .SetValidator(new StorageAccountOptionsValidator());
    }
}

internal class ContainerNamesQueryHandler : IRequestHandler<ContainerNamesQuery, IAsyncEnumerable<string>>
{
    private readonly IStorageAccountFactory              storageAccountFactory;
    private readonly ILogger<ContainerNamesQueryHandler> logger;

    public ContainerNamesQueryHandler(IStorageAccountFactory storageAccountFactory, ILogger<ContainerNamesQueryHandler> logger)
    {
        this.storageAccountFactory = storageAccountFactory;
        this.logger                = logger;
    }

    public async Task<IAsyncEnumerable<string>> Handle(ContainerNamesQuery request, CancellationToken cancellationToken)
    {
        await new ContainerNamesQueryValidator().ValidateAndThrowAsync(request, cancellationToken);

        return GetContainerNames(cancellationToken);

        async IAsyncEnumerable<string> GetContainerNames([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var credentials    = new StorageAccountOptions{ AccountName = request.AccountName, AccountKey = request.AccountKey };
            var storageAccount = storageAccountFactory.Create(credentials);

            await foreach (var container in storageAccount.ListContainers(cancellationToken))
            {
                yield return container.Name;
            }
        }
    }
}