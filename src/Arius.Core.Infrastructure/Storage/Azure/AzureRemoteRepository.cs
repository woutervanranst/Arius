using Arius.Core.Domain;
using Arius.Core.Domain.Services;
using Arius.Core.Domain.Storage;
using Arius.Core.Domain.Storage.FileSystem;
using Azure;
using Azure.Storage.Blobs;
using System.Net;

namespace Arius.Core.Infrastructure.Storage.Azure;

internal class AzureRemoteRepository : IRemoteRepository
{
    private readonly string                         passphrase;
    private readonly ICryptoService                 cryptoService;
    private readonly ILogger<AzureRemoteRepository> logger;

    internal AzureContainerFolder StateDatabaseFolder    { get; }
    //internal AzureContainerFolder ChunkListsFolder       { get; }
    internal AzureContainerFolder ChunksFolder           { get; }
    internal AzureContainerFolder RehydratedChunksFolder { get; }

    internal const string STATE_DBS_FOLDER_NAME         = "states";
    //internal const string CHUNK_LISTS_FOLDER_NAME       = "chunklists";
    internal const string CHUNKS_FOLDER_NAME            = "chunks";
    internal const string REHYDRATED_CHUNKS_FOLDER_NAME = "chunks-rehydrated";

    public AzureRemoteRepository(BlobContainerClient blobContainerClient, string passphrase, ICryptoService cryptoService, ILogger<AzureRemoteRepository> logger)
    {
        this.passphrase    = passphrase;
        this.cryptoService = cryptoService;
        this.logger        = logger;

        StateDatabaseFolder = new AzureContainerFolder(blobContainerClient, STATE_DBS_FOLDER_NAME);
        //ChunkListsFolder       = new AzureContainerFolder(blobContainerClient, CHUNK_LISTS_FOLDER_NAME);
        ChunksFolder           = new AzureContainerFolder(blobContainerClient, CHUNKS_FOLDER_NAME);
        RehydratedChunksFolder = new AzureContainerFolder(blobContainerClient, REHYDRATED_CHUNKS_FOLDER_NAME);
    }

    public IAsyncEnumerable<RepositoryVersion> GetStateDatabaseVersions()
    {
        return StateDatabaseFolder.GetBlobs().Select(blob => new RepositoryVersion { Name = blob.Name });
    }

    public IBlob GetStateDatabaseBlobForVersion(RepositoryVersion version)
    {
        return StateDatabaseFolder.GetBlob(version.Name);
    }

    public async Task UploadStateDatabaseAsync(IStateDatabaseFile file, RepositoryVersion version, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Uploading State Database {version}...", version.Name);

        var blob     = StateDatabaseFolder.GetBlob(version.Name);
        var metadata = AzureBlob.CreateStateDatabaseMetadata();

        await UploadAsync(file, blob, metadata, cancellationToken);

        await blob.SetStorageTierAsync(StorageTier.Cold);

        logger.LogInformation("Uploading State Database {version}... done", version.Name);
    }

    public async Task<BinaryProperties> UploadBinaryFileAsync(IBinaryFileWithHash file, Func<long, StorageTier> effectiveTier, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Uploading Binary {hash}...", file.Hash);

        var blob        = ChunksFolder.GetBlob(file.Hash.Value.BytesToHexString());
        var metadata = AzureBlob.CreateChunkMetadata(file.Length);

        var r = await UploadAsync(file, blob, metadata, cancellationToken);
        
        var t = effectiveTier(r.archivedLength);
        await blob.SetStorageTierAsync(t);

        var bp = new BinaryProperties
        {
            Hash         = file.Hash,
            OriginalSize = r.originalLength,
            ArchivedSize = r.archivedLength,
            StorageTier  = t
        };

        logger.LogInformation("Uploading Binary {hash}... done", file.Hash.ToShortString());

        return bp;
    }

    private async Task<(long originalLength, long archivedLength)> UploadAsync(IFile source, AzureBlob target, IDictionary<string, string> metadata, CancellationToken cancellationToken = default)
    {
        RestartUpload:

        try
        {
            await using var ss       = source.OpenRead();
            await using var ts = await target.OpenWriteAsync(
                contentType: ICryptoService.ContentType,
                metadata: metadata,
                throwOnExists: true,
                cancellationToken: cancellationToken);
            await cryptoService.CompressAndEncryptAsync(ss, ts, passphrase);

            return (ss.Length, ts.Position); // ts.Length is not supported
        }
        catch (RequestFailedException rfe) when (rfe.Status == (int)HttpStatusCode.Conflict)
        {
            // The blob already exists
            if (await target.GetContentTypeAsync() != ICryptoService.ContentType || await target.GetContentLengthAsync() == 0)
            {
                logger.LogWarning($"Corrupt Binary {target.FullName}. Deleting and uploading again");
                await target.DeleteAsync();

                goto RestartUpload;
            }
            else
            {
                // graceful handling if the chunk is already uploaded but it does not yet exist in the database
                logger.LogWarning($"A valid Binary '{target.FullName}' already existed, perhaps from a previous/crashed run?");

                return (await target.GetOriginalContentLengthAsync() ?? 0, await target.GetContentLengthAsync());
            }
        }
    }

    public async Task DownloadAsync(IBlob blob, IFile file, CancellationToken cancellationToken = default)
    {
        if (blob is AzureBlob azureBlob)
            await DownloadAsync(azureBlob, file, cancellationToken);
        else
            throw new NotSupportedException($"'{blob.GetType()}' is not supported");
    }

    public async Task SetBinaryStorageTierAsync(Hash hash, StorageTier effectiveTier, CancellationToken cancellationToken = default)
    {
        var b = ChunksFolder.GetBlob(hash.Value.BytesToHexString());

        await b.SetStorageTierAsync(effectiveTier);
    }

    private async Task DownloadAsync(AzureBlob blob, IFile file, CancellationToken cancellationToken = default)
    {
        await using var ss = await blob.OpenReadAsync(cancellationToken);
        await using var ts = file.OpenWrite();
        await cryptoService.DecryptAndDecompressAsync(ss, ts, passphrase);

        logger.LogInformation("Successfully downloaded latest state '{blob}' to '{file}'", blob.Name, file);
    }
}