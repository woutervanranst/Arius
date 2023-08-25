using Arius.Core.Models;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Arius.Core.Repositories.BlobRepository;

internal abstract class BlobContainerFolder<TBlob> where TBlob : Blob
{
    protected readonly BlobContainerClient container;
    protected readonly string              folderName;

    protected BlobContainerFolder(BlobContainerClient container, string folderName)
    {
        this.container  = container;
        this.folderName = folderName;
    }

    private string GetBlobFullName(string name) => $"{folderName}/{name}";

    /// <summary>
    /// Get an (existing or not existing) Blob
    /// Check whether the blob exists through the Exists property
    /// </summary>
    public TBlob GetBlob(string name) => GetBlob<TBlob>(name);
    protected TBlob GetBlob<T>(string name)
    {
        var bbc = container.GetBlockBlobClient(GetBlobFullName(name));
        return CreateAzureBlob(bbc);
    }

    protected virtual TBlob CreateAzureBlob(BlockBlobClient client) => (TBlob)new Blob(client);
}

internal class StateContainerFolder : BlobContainerFolder<Blob>
{
    public StateContainerFolder(BlobContainerClient container, string folderName) : base(container, folderName)
    {
    }

    /// <summary>
    /// List all existing blobs
    /// </summary>
    public virtual IAsyncEnumerable<(string Name, AccessTier? AccessTier)> GetBlobsAsync() // NOTE this is purposefully only in this Folder -- for the other folders we rely on the backing db
    {
        return container.GetBlobsAsync(prefix: $"{folderName}/").Select(bi => (Path.GetFileName(bi.Name), bi.Properties.AccessTier));
    }
}

internal class ChunkBlobContainerFolder : BlobContainerFolder<ChunkBlob>
{
    public ChunkBlobContainerFolder(BlobContainerClient containter, string folderName) : base(containter, folderName)
    {
    }

    public ChunkBlob GetBlob(ChunkHash chunkHash) => base.GetBlob<ChunkBlob>(chunkHash.Value.BytesToHexString());

    protected override ChunkBlob CreateAzureBlob(BlockBlobClient client) => new ChunkBlob(client);
}

internal class RehydratedChunkBlobContainerFolder : ChunkBlobContainerFolder
{
    public RehydratedChunkBlobContainerFolder(BlobContainerClient containter, string folderName) : base(containter, folderName)
    {
    }

    /// <summary>
    /// List all rehydrated/rehydrating blobs
    /// </summary>
    public virtual IAsyncEnumerable<(string Name, ArchiveStatus? ArchiveStatus)> GetBlobsAsync()
    {
        return container.GetBlobsAsync(prefix: $"{folderName}/").Select(bi => (Path.GetFileName(bi.Name), bi.Properties.ArchiveStatus));
    }

    public async Task DeleteFolderAsync()
    {
        await foreach (var b in container.GetBlobsAsync(prefix: $"{folderName}/"))
            await container.DeleteBlobAsync(b.Name);
    }
}

internal class ChunkListBlobContainerFolder : BlobContainerFolder<ChunkListBlob>
{
    public ChunkListBlobContainerFolder(BlobContainerClient containter, string folderName) : base(containter, folderName)
    {
    }

    public ChunkListBlob GetBlob(BinaryHash chunkHash) => base.GetBlob<ChunkListBlob>(chunkHash.Value.BytesToHexString());

    protected override ChunkListBlob CreateAzureBlob(BlockBlobClient client) => new ChunkListBlob(client);
}