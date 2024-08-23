using Arius.Core.Domain.Storage;
using Arius.Core.New.Services;
using Azure.Core;
using MediatR;

namespace Arius.Core.New.Commands.DownloadStateDb;

public abstract record DownloadStateDbCommandBase : IRequest<Unit>
{
    public required RepositoryOptions Repository { get; init; }
    public required string            LocalPath  { get; init; }
}

public record DownloadLatestStateDbCommand : DownloadStateDbCommandBase
{
}

public record DownloadStateDbCommand : DownloadStateDbCommandBase
{
    public required RepositoryVersion Version    { get; init; }
}

internal class DownloadStateDbCommandHandler : IRequestHandler<DownloadLatestStateDbCommand, Unit>, IRequestHandler<DownloadStateDbCommand, Unit>
{
    private readonly IStorageAccountFactory                storageAccountFactory;
    private readonly ILogger<DownloadLatestStateDbCommand> logger;

    public DownloadStateDbCommandHandler(IStorageAccountFactory storageAccountFactory, ILogger<DownloadLatestStateDbCommand> logger)
    {
        this.storageAccountFactory = storageAccountFactory;
        this.logger                = logger;
    }
    public async Task<Unit> Handle(DownloadLatestStateDbCommand request, CancellationToken cancellationToken)
    {
        var repository = storageAccountFactory.GetRepository(request.Repository);

        var latestRepositoryVersion = await repository
            .GetRepositoryVersions()
            .OrderBy(b => b.Name)
            .LastOrDefaultAsync(cancellationToken: cancellationToken);

        if (latestRepositoryVersion == null) // TODO test this
            return Unit.Value;

        var blob = repository.GetRepositoryVersionBlob(latestRepositoryVersion);

        await DownloadAsync(blob, request.LocalPath, request.Repository.Passphrase, cancellationToken);

        return Unit.Value;

    }

    public async Task<Unit> Handle(DownloadStateDbCommand request, CancellationToken cancellationToken)
    {
        var repository = storageAccountFactory.GetRepository(request.Repository);

        var blob = repository.GetRepositoryVersionBlob(request.Version);
        
        await DownloadAsync(blob, request.LocalPath, request.Repository.Passphrase, cancellationToken);

        return Unit.Value;
    }

    private async Task DownloadAsync(IBlob blob, string localPath, string passphrase, CancellationToken cancellationToken)
    {
        await using var ss         = await blob.OpenReadAsync(cancellationToken);
        await using var ts = File.Create(localPath);
        await CryptoService.DecryptAndDecompressAsync(ss, ts, passphrase);

        logger.LogInformation($"Successfully downloaded latest state '{blob.Name}' to '{localPath}'");
    }
}