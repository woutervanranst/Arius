using Arius.Core.Domain.Storage;
using MediatR;

namespace Arius.Core.New.Queries.GetStateDbVersions;

public record GetRepositoryVersionsQuery : IRequest<IAsyncEnumerable<RepositoryVersion>>
{
    public required RepositoryOptions Repository { get; init; }
}

internal class GetRepositoryVersionsQueryHandler : IRequestHandler<GetRepositoryVersionsQuery, IAsyncEnumerable<RepositoryVersion>>
{
    private readonly IStorageAccountFactory storageAccountFactory;

    public GetRepositoryVersionsQueryHandler(IStorageAccountFactory storageAccountFactory)
    {
        this.storageAccountFactory = storageAccountFactory;
    }

    public async Task<IAsyncEnumerable<RepositoryVersion>> Handle(GetRepositoryVersionsQuery request, CancellationToken cancellationToken)
    {
        return storageAccountFactory.GetRepository(request.Repository).GetRepositoryVersions();
    }
}