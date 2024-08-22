using Arius.Core.Domain.Storage;
using MediatR;

namespace Arius.Core.New.Commands.DownloadStateDb;

public interface IDownloadStateDbCommand : IRequest<Unit>
{
    RepositoryOptions Repository { get; init; }
    string            LocalPath  { get; init; }
}

public record DownloadLatestStateDbCommand : IDownloadStateDbCommand
{
    public required RepositoryOptions Repository { get; init; }
    public required string            LocalPath  { get; init; }
}

public record DownloadStateDbCommand : IDownloadStateDbCommand
{
    public required RepositoryOptions Repository { get; init; }
    public required string            LocalPath  { get; init; }
    public required RepositoryVersion Version    { get; init; }
}

internal class DownloadStateDbCommandHandler : IRequestHandler<IDownloadStateDbCommand, Unit>
{
    private readonly IStorageAccountFactory storageAccountFactory;

    public DownloadStateDbCommandHandler(IStorageAccountFactory storageAccountFactory)
    {
        this.storageAccountFactory = storageAccountFactory;
    }
    public async Task<Unit> Handle(IDownloadStateDbCommand request, CancellationToken cancellationToken)
    {
        var repository = storageAccountFactory.GetRepository(request.Repository);

        var blob = await GetStateDbBlobAsync();



        throw new NotImplementedException();


        async Task<IBlob?> GetStateDbBlobAsync()
        {
            if (request is DownloadLatestStateDbCommand)
            {
                var latestRepositoryVersion = await repository
                    .GetRepositoryVersions()
                    .OrderBy(b => b.Name)
                    .LastOrDefaultAsync(cancellationToken: cancellationToken);

                if (latestRepositoryVersion == null) // TODO test this
                    return null;

                return repository.GetRepositoryVersionBlob(latestRepositoryVersion);
            }
            if (request is DownloadStateDbCommand downloadStateDbCommand)
            {
                return repository.GetRepositoryVersionBlob(downloadStateDbCommand.Version);
            }

            throw new ArgumentException("Command not supported");
        }
    }

}