using Arius.Core.Domain;
using Arius.Core.Domain.Repositories;
using Arius.Core.Domain.Services;
using Arius.Core.Domain.Storage;
using Arius.Core.Domain.Storage.FileSystem;
using Arius.Core.Infrastructure.Repositories;
using Azure.Storage.Blobs;

namespace Arius.Core.Infrastructure.Storage.Azure;

internal class AzureRemoteRepository : IRemoteRepository
{
    private readonly ILoggerFactory                 loggerFactory;
    private readonly ILogger<AzureRemoteRepository> logger;

    internal const string STATE_DBS_FOLDER_NAME         = "states";
    internal const string CHUNKS_FOLDER_NAME            = "chunks";
    internal const string REHYDRATED_CHUNKS_FOLDER_NAME = "chunks-rehydrated";

    public AzureRemoteRepository(
        BlobContainerClient blobContainerClient,
        RemoteRepositoryOptions remoteRepositoryOptions,
        ICryptoService cryptoService,
        ILoggerFactory loggerFactory,
        ILogger<AzureRemoteRepository> logger)
    {
        this.loggerFactory           = loggerFactory;
        this.logger                  = logger;

        StateDatabaseFolder    = new AzureContainerFolder(blobContainerClient, remoteRepositoryOptions, STATE_DBS_FOLDER_NAME,         cryptoService, loggerFactory.CreateLogger("AzureStateDbContainerFolder"));
        ChunksFolder           = new AzureContainerFolder(blobContainerClient, remoteRepositoryOptions, CHUNKS_FOLDER_NAME,            cryptoService, loggerFactory.CreateLogger("AzureChunksContainerFolder"));
        RehydratedChunksFolder = new AzureContainerFolder(blobContainerClient, remoteRepositoryOptions, REHYDRATED_CHUNKS_FOLDER_NAME, cryptoService, loggerFactory.CreateLogger("AzureRehydratedChunksContainerFolder"));
    }

    public IRemoteStateRepository GetRemoteStateRepository()
    {
        return new SqliteRemoteStateRepository(StateDatabaseFolder, loggerFactory, loggerFactory.CreateLogger<SqliteRemoteStateRepository>());
    }

    
    internal AzureContainerFolder StateDatabaseFolder    { get; }
    internal AzureContainerFolder ChunksFolder           { get; }
    internal AzureContainerFolder RehydratedChunksFolder { get; }

    


    

    public async Task<BinaryProperties> UploadBinaryFileAsync(IBinaryFileWithHash file, Func<long, StorageTier> effectiveTier, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Uploading Binary {hash}...", file.Hash);

        var blob        = ChunksFolder.GetBlob(file.Hash.Value.BytesToHexString());
        var metadata = AzureBlob.CreateChunkMetadata(file.Length);

        var r = await ChunksFolder.UploadAsync(file, blob, metadata, cancellationToken);
        
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



    public async Task SetBinaryStorageTierAsync(Hash hash, StorageTier effectiveTier, CancellationToken cancellationToken = default)
    {
        var b = ChunksFolder.GetBlob(hash.Value.BytesToHexString());

        await b.SetStorageTierAsync(effectiveTier);
    }
}