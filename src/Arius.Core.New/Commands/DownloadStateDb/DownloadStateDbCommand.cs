using Arius.Core.Domain.Storage;
using Arius.Core.New.Services;
using Azure;
using MediatR;

namespace Arius.Core.New.Commands.DownloadStateDb;

public record DownloadStateDbCommand : IRequest<DownloadStateDbCommandResult>
{
    public required RepositoryOptions  Repository { get; init; }
    public          RepositoryVersion? Version    { get; init; }
    public required string             LocalPath  { get; init; }
}

public record DownloadStateDbCommandResult
{
    public required DownloadStateDbCommandResultType Type    { get; init; }
    public          RepositoryVersion?               Version { get; init; }
}

public enum DownloadStateDbCommandResultType
{
    NoStatesYet,
    LatestDownloaded,
    RequestedVersionDownloaded
}

internal class DownloadStateDbCommandHandler : IRequestHandler<DownloadStateDbCommand, DownloadStateDbCommandResult>
{
    private readonly IStorageAccountFactory                 storageAccountFactory;
    private readonly ICryptoService                         cryptoService;
    private readonly ILogger<DownloadStateDbCommandHandler> logger;

    public DownloadStateDbCommandHandler(IStorageAccountFactory storageAccountFactory, ICryptoService cryptoService, ILogger<DownloadStateDbCommandHandler> logger)
    {
        this.storageAccountFactory = storageAccountFactory;
        this.cryptoService         = cryptoService;
        this.logger                = logger;
    }

    public async Task<DownloadStateDbCommandResult> Handle(DownloadStateDbCommand request, CancellationToken cancellationToken)
    {
        var repository = storageAccountFactory.GetRepository(request.Repository);

        if (request.Version is null)
        {
            // Download the latest version
            var latestRepositoryVersion = await repository
                .GetRepositoryVersions()
                .OrderBy(b => b.Name)
                .LastOrDefaultAsync(cancellationToken: cancellationToken);

            if (latestRepositoryVersion == null)
                return new DownloadStateDbCommandResult
                {
                    Type = DownloadStateDbCommandResultType.NoStatesYet
                };
            else
            {
                var blob = repository.GetRepositoryVersionBlob(latestRepositoryVersion);

                await DownloadAsync(blob, request.LocalPath, request.Repository.Passphrase, cancellationToken);

                return new DownloadStateDbCommandResult
                {
                    Type    = DownloadStateDbCommandResultType.LatestDownloaded,
                    Version = latestRepositoryVersion
                };
            }
        }
        else
        {
            // Download the requested version
            try
            {
                var blob = repository.GetRepositoryVersionBlob(request.Version);

                await DownloadAsync(blob, request.LocalPath, request.Repository.Passphrase, cancellationToken);

                return new DownloadStateDbCommandResult
                {
                    Type    = DownloadStateDbCommandResultType.RequestedVersionDownloaded,
                    Version = request.Version
                };
            }
            catch (RequestFailedException e) when (e.ErrorCode == "BlobNotFound")
            {
                throw new ArgumentException("The requested version was not found", nameof(request.Version), e);
            }
        }
    }

    private async Task DownloadAsync(IBlob blob, string localPath, string passphrase, CancellationToken cancellationToken)
    {
        await using var ss = await blob.OpenReadAsync(cancellationToken);
        await using var ts = File.Create(localPath);
        await cryptoService.DecryptAndDecompressAsync(ss, ts, passphrase);

        logger.LogInformation($"Successfully downloaded latest state '{blob.Name}' to '{localPath}'");
    }
}