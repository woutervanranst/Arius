using Arius.Core.Domain.Storage;
using MediatR;

namespace Arius.Core.New.Queries.GetStateDbVersions;

public record GetRepositoryVersionsQuery : IStreamRequest<RepositoryVersion>
{
    public required RepositoryOptions Repository { get; init; }
}

internal class GetRepositoryVersionsQueryHandler : IStreamRequestHandler<GetRepositoryVersionsQuery, RepositoryVersion>
{
    private readonly IStorageAccountFactory storageAccountFactory;

    public GetRepositoryVersionsQueryHandler(IStorageAccountFactory storageAccountFactory)
    {
        this.storageAccountFactory = storageAccountFactory;
    }

    public IAsyncEnumerable<RepositoryVersion> Handle(GetRepositoryVersionsQuery request, CancellationToken cancellationToken)
    {
        return storageAccountFactory.GetRepository(request.Repository).GetRepositoryVersions();
    }
}