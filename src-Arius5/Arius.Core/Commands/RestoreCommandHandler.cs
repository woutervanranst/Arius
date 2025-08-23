using Arius.Core.Extensions;
using Arius.Core.Models;
using Arius.Core.Repositories;
using Arius.Core.Services;
using Mediator;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Zio;
using Zio.FileSystems;
using System.Threading.Tasks;

namespace Arius.Core.Commands;

internal class RestoreCommandHandler : ICommandHandler<RestoreCommand>
{
    private readonly ILogger<RestoreCommandHandler> logger;
    private readonly IOptions<AriusConfiguration> config;

    public RestoreCommandHandler(
        ILogger<RestoreCommandHandler> logger,
        IOptions<AriusConfiguration> config)
    {
        this.logger = logger;
        this.config = config;
    }

    public async ValueTask<Unit> Handle(RestoreCommand request, CancellationToken cancellationToken)
    {
        var handlerContext = await HandlerContext.CreateAsync(request);

        var chunkHash = (Hash)"3e12370e300aef3a239a8a063dc618e581f8f1f5e16f690ed73b3ca5d627369e";
        var targetFilePath = Path.GetTempFileName();

        logger.LogInformation($"Restoring chunk '{chunkHash.ToShortString()}' to '{targetFilePath}'");

        try
        {
            // 1. Get the blob from storage
            await using var blobStream = await handlerContext.BlobStorage.OpenReadChunkAsync(chunkHash, cancellationToken);

            // 2. Get the decrypted and decompressed stream
            await using var decryptionStream = await blobStream.GetDecryptionStreamAsync(handlerContext.Request.Passphrase, cancellationToken);

            // 3. Write to the target file
            await using var targetFileStream = File.OpenWrite(targetFilePath);
            await decryptionStream.CopyToAsync(targetFileStream, cancellationToken);
            await targetFileStream.FlushAsync(cancellationToken); // Explicitly flush

            logger.LogInformation($"Successfully restored chunk '{chunkHash.ToShortString()}' to '{targetFilePath}'");
        }
        catch (Exception e)
        {
            logger.LogError(e, $"Error restoring chunk '{chunkHash.ToShortString()}'");
            throw;
        }

        return Unit.Value;
    }

    private class HandlerContext
    {
        public static async Task<HandlerContext> CreateAsync(RestoreCommand request)
        {
            var bs = await GetBlobStorageAsync();
            var sr = await GetStateRepositoryAsync(bs);

            return new HandlerContext
            {
                Request = request,
                BlobStorage = bs,
                StateRepo = sr,
                Hasher = new Sha256Hasher(request.Passphrase),
                FileSystem = GetFileSystem()
            };

            async Task<BlobStorage> GetBlobStorageAsync()
            {
                request.ProgressReporter?.Report(new TaskProgressUpdate($"Creating blob container '{request.ContainerName}'...", 0));

                var bs = new BlobStorage(request.AccountName, request.AccountKey, request.ContainerName);
                var created = await bs.CreateContainerIfNotExistsAsync();

                request.ProgressReporter?.Report(new TaskProgressUpdate($"Creating blob container '{request.ContainerName}'...", 100, created ? "Created" : "Already existed"));

                return bs;
            }

            async Task<StateRepository> GetStateRepositoryAsync(BlobStorage bs)
            {
                request.ProgressReporter?.Report(new TaskProgressUpdate($"Initializing state repository...", 0));

                throw new NotImplementedException();

                //try
                //{
                //    return new StateRepository();
                //}
                //catch (Exception e)
                //{
                //    throw;
                //}

                request.ProgressReporter?.Report(new TaskProgressUpdate($"Initializing state repository...", 100, "Done"));
            }

            FilePairFileSystem GetFileSystem()
            {
                var pfs = new PhysicalFileSystem();
                var root = pfs.ConvertPathFromInternal(request.LocalRoot.FullName);
                var sfs = new SubFileSystem(pfs, root, true);
                return new FilePairFileSystem(sfs, true);
            }
        }

        public required RestoreCommand Request { get; init; }
        public required BlobStorage BlobStorage { get; init; }
        public required StateRepository StateRepo { get; init; }
        public required Sha256Hasher Hasher { get; init; }
        public required FilePairFileSystem FileSystem { get; init; }
    }
}
