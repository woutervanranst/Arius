using System.Runtime.CompilerServices;
using Arius.Core.Domain.Storage;
using FluentValidation;
using MediatR;

namespace Arius.Core.New.Queries.ContainerNames;

public record ContainerNamesQuery : IStreamRequest<string>
{
    public required StorageAccountOptions StorageAccount { get; init; }
}


internal class ContainerNamesQueryValidator : AbstractValidator<ContainerNamesQuery>
{
    public ContainerNamesQueryValidator()
    {
        RuleFor(query => new StorageAccountOptions
            {
                AccountName = query.StorageAccount.AccountName,
                AccountKey  = query.StorageAccount.AccountKey
            })
            .SetValidator(new StorageAccountOptionsValidator());
    }
}

internal class ContainerNamesQueryHandler : IStreamRequestHandler<ContainerNamesQuery, string>
{
    private readonly IStorageAccountFactory              storageAccountFactory;
    private readonly ILogger<ContainerNamesQueryHandler> logger;

    public ContainerNamesQueryHandler(IStorageAccountFactory storageAccountFactory, ILogger<ContainerNamesQueryHandler> logger)
    {
        this.storageAccountFactory = storageAccountFactory;
        this.logger                = logger;
    }

    public async IAsyncEnumerable<string> Handle(ContainerNamesQuery request, CancellationToken cancellationToken)
    {
        await new ContainerNamesQueryValidator().ValidateAndThrowAsync(request, cancellationToken);

        var credentials    = new StorageAccountOptions { AccountName = request.StorageAccount.AccountName, AccountKey = request.StorageAccount.AccountKey };
        var storageAccount = storageAccountFactory.GetStorageAccount(credentials);

        await foreach (var container in storageAccount.GetContainers(cancellationToken))
        {
            yield return container.Name;
        }
    }
}