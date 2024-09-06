using Arius.Core.Domain;
using Arius.Core.Domain.Services;
using Arius.Core.Domain.Storage;
using Arius.Core.Domain.Storage.FileSystem;
using Azure.Storage.Blobs;

namespace Arius.Core.Infrastructure.Storage.Azure;

internal class AzureRepository : IRepository
{
    private readonly BlobContainerClient      blobContainerClient;
    private readonly string                   passphrase;
    private readonly ICryptoService           cryptoService;
    private readonly ILogger<AzureRepository> logger;

    internal AzureContainerFolder StateFolder            { get; }
    internal AzureContainerFolder ChunkListsFolder       { get; }
    internal AzureContainerFolder ChunksFolder           { get; }
    internal AzureContainerFolder RehydratedChunksFolder { get; }

    private const string STATE_DBS_FOLDER_NAME         = "states";
    private const string CHUNK_LISTS_FOLDER_NAME       = "chunklists";
    private const string CHUNKS_FOLDER_NAME            = "chunks";
    private const string REHYDRATED_CHUNKS_FOLDER_NAME = "chunks-rehydrated";

    public AzureRepository(BlobContainerClient blobContainerClient, string passphrase, ICryptoService cryptoService, ILogger<AzureRepository> logger)
    {
        this.blobContainerClient = blobContainerClient;
        this.passphrase          = passphrase;
        this.cryptoService       = cryptoService;
        this.logger              = logger;

        StateFolder            = new AzureContainerFolder(blobContainerClient, STATE_DBS_FOLDER_NAME);
        ChunkListsFolder       = new AzureContainerFolder(blobContainerClient, CHUNK_LISTS_FOLDER_NAME);
        ChunksFolder           = new AzureContainerFolder(blobContainerClient, CHUNKS_FOLDER_NAME);
        RehydratedChunksFolder = new AzureContainerFolder(blobContainerClient, REHYDRATED_CHUNKS_FOLDER_NAME);
    }

    public IAsyncEnumerable<RepositoryVersion> GetRepositoryVersions()
    {
        return StateFolder.GetBlobs().Select(blob => new RepositoryVersion { Name = blob.Name });
    }

    public IBlob GetRepositoryVersionBlob(RepositoryVersion repositoryVersion)
    {
        return StateFolder.GetBlob(repositoryVersion.Name);
    }

    public async Task<BinaryProperties> UploadChunkAsync(IBinaryFileWithHash file, CancellationToken cancellationToken = default)
    {
        var b = ChunksFolder.GetBlob(file.Hash.Value.BytesToHexString());

        var r = await UploadAsync(file, b, cancellationToken);

        var bp = new BinaryProperties
        {
            Hash              = file.Hash,
            OriginalLength    = r.originalLength,
            ArchivedLength    = r.archivedLength,
            IncrementalLength = 0, // TODO
            StorageTier       = StorageTier.Hot // TODO
        };


        // TODO Set contenttype, metadata, accesstier, ...

        return bp;
    }

    private async Task<(long originalLength, long archivedLength)> UploadAsync(IFile source, AzureBlob target, CancellationToken cancellationToken = default)
    {
        await using var ss       = source.OpenRead();
        var             metadata = AzureBlob.CreateMetadata(ss.Length);
        await using var ts       = await target.OpenWriteAsync(cancellationToken: cancellationToken);
        await cryptoService.CompressAndEncryptAsync(ss, ts, passphrase);

        return (ss.Length, ts.Position); // ts.Length is not supported
    }

    public async Task DownloadAsync(IBlob blob, IFile file, CancellationToken cancellationToken = default)
    {
        if (blob is AzureBlob azureBlob)
            await DownloadAsync(azureBlob, file, cancellationToken);
        else
            throw new NotSupportedException($"'{blob.GetType()}' is not supported");
    }

    public async Task DownloadAsync(AzureBlob blob, IFile file, CancellationToken cancellationToken = default)
    {
        await using var ss = await blob.OpenReadAsync(cancellationToken);
        await using var ts = file.OpenWrite();
        await cryptoService.DecryptAndDecompressAsync(ss, ts, passphrase);

        logger.LogInformation("Successfully downloaded latest state '{blob}' to '{file}'", blob.Name, file);
    }
}