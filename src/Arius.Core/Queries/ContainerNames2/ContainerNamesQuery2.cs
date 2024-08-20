using Arius.Core.Facade;
using MediatR;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Arius.Core.Queries.ContainerNames2;

public record ContainerNamesQuery2 : IRequest<IAsyncEnumerable<string>>, IStorageAccountOptions
{
    public required string AccountName { get; init; }
    public required string AccountKey  { get; init; }
    public required int    MaxRetries  { get; init; }
}

internal class ContainerNamesQuery2Handler : IRequestHandler<ContainerNamesQuery2, IAsyncEnumerable<string>>
{
    private readonly ILogger<ContainerNamesQuery2Handler> logger;

    public ContainerNamesQuery2Handler(ILogger<ContainerNamesQuery2Handler> logger)
    {
        this.logger = logger;
    }

    public async Task<IAsyncEnumerable<string>> Handle(ContainerNamesQuery2 request, CancellationToken cancellationToken)
    {
        return GetContainerNames(cancellationToken);


        async IAsyncEnumerable<string> GetContainerNames([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var sao = new StorageAccountOptions(request.AccountName, request.AccountKey);

            var bsc = sao.GetBlobServiceClient(request.MaxRetries, TimeSpan.FromSeconds(5));

            await foreach (var container in bsc.GetBlobContainersAsync(cancellationToken: cancellationToken))
            {
                yield return container.Name;
            }
        }
    }
}