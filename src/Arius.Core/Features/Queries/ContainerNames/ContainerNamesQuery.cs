using Mediator;
using System.Runtime.CompilerServices;

namespace Arius.Core.Features.Queries.ContainerNames;

public sealed record ContainerNamesQuery : StorageAccountCommandProperties, IStreamCommand<string>
{
}

internal class ContainerNamesQueryHandler : IStreamCommandHandler<ContainerNamesQuery, string>
{
    public ContainerNamesQueryHandler()
    {
    }
    public async IAsyncEnumerable<string> Handle(ContainerNamesQuery request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        //var storageAccount = _storageAccountFactory.GetStorageAccount(request.StorageAccountName);
        //var storage        = storageAccount.GetRepositoryStorage(request.RepositoryName);
        //await foreach (var name in storage.GetNamesAsync(string.Empty, cancellationToken).WithCancellation(cancellationToken))
        //{
        //    yield return name;
        //}

        yield return "a";
        yield return "b";
    }
}