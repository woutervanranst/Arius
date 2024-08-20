using Arius.Core.Facade;
using MediatR;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Arius.Core.Queries.ContainerNames2;

public record ContainerNamesQuery2 : IRequest<IAsyncEnumerable<string>>, IStorageAccountOptions
{
    public required string AccountName { get; init; }
    public required string AccountKey  { get; init; }
    public required int    MaxRetries  { get; init; }
}

public class ContainerNamesQueryHandler : IRequestHandler<ContainerNamesQuery2, IAsyncEnumerable<string>>
{
    private readonly ILogger<ContainerNamesQueryHandler> logger;

    public ContainerNamesQueryHandler(ILogger<ContainerNamesQueryHandler> logger)
    {
        this.logger = logger;
    }

    public async Task<IAsyncEnumerable<string>> Handle(ContainerNamesQuery2 request, CancellationToken cancellationToken)
    {
        return GetContainerNamesAsync(cancellationToken);
    }

    private async IAsyncEnumerable<string> GetContainerNamesAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return "a";
        yield return "b";
    }
}