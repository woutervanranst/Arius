using Arius.Core.Domain.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using System.Linq;

namespace Arius.Core.Infrastructure.Storage.Azure;

internal class AzureRepository : IRepository
{
    private readonly BlobContainerClient blobContainerClient;
    private readonly string              passphrase;

    private readonly AzureContainerFolder StateFolder;
    private readonly AzureContainerFolder ChunkListsFolder;
    private readonly AzureContainerFolder ChunksFolder;
    private readonly AzureContainerFolder RehydratedChunksFolder;

    private const string STATE_DBS_FOLDER_NAME         = "states";
    private const string CHUNK_LISTS_FOLDER_NAME       = "chunklists";
    private const string CHUNKS_FOLDER_NAME            = "chunks";
    private const string REHYDRATED_CHUNKS_FOLDER_NAME = "chunks-rehydrated";

    public AzureRepository(/*IContainer container, */BlobContainerClient blobContainerClient, string passphrase)
    {
        this.blobContainerClient = blobContainerClient;
        this.passphrase          = passphrase;
        //Container                = container;

        StateFolder            = new AzureContainerFolder(blobContainerClient, STATE_DBS_FOLDER_NAME);
        ChunkListsFolder       = new AzureContainerFolder(blobContainerClient, CHUNK_LISTS_FOLDER_NAME);
        ChunksFolder           = new AzureContainerFolder(blobContainerClient, CHUNKS_FOLDER_NAME);
        RehydratedChunksFolder = new AzureContainerFolder(blobContainerClient, REHYDRATED_CHUNKS_FOLDER_NAME);
    }
    //public IContainer Container  { get; }

    //public async Task<IEnumerable<string>> ListBlobsAsync(CancellationToken cancellationToken = default)
    //{
    //    var blobs = new List<string>();

    //    await foreach (var blobItem in blobContainerClient.GetBlobs(cancellationToken: cancellationToken))
    //    {
    //        blobs.Add(blobItem.Name);
    //    }

    //    return blobs;
    //}

    public IAsyncEnumerable<RepositoryVersion> GetRepositoryVersions()
    {
        return StateFolder.GetBlobs().Select(blob => new RepositoryVersion(blob.Name, blob.AccessTier.ToStorageTier()));
    }
}

internal class AzureContainerFolder
{
    private readonly BlobContainerClient blobContainerClient;
    private readonly string              folderName;

    public AzureContainerFolder(BlobContainerClient blobContainerClient, string folderName)
    {
        this.blobContainerClient = blobContainerClient;
        this.folderName          = folderName;
    }

    public IAsyncEnumerable<AzureBlob> GetBlobs()
    {
        return blobContainerClient
            .GetBlobsAsync(prefix: $"{folderName}/")
            .Select(bi => new AzureBlob(bi));
    }
}

internal class AzureBlob
{
    private readonly BlobItem blobItem;

    public AzureBlob(BlobItem blobItem)
    {
        this.blobItem = blobItem;
    }

    public string      Name       => blobItem.Name;
    public AccessTier? AccessTier => blobItem.Properties.AccessTier;
}