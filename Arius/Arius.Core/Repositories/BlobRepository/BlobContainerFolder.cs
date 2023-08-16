using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Arius.Core.Models;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;

namespace Arius.Core.Repositories.BlobRepository;

internal class BlobContainerFolder<TEntry, TBlob> where TEntry : BlobEntry where TBlob : Blob
{
    private readonly BlobContainerClient container;
    private readonly string              folderName;

    public BlobContainerFolder(BlobContainerClient container, string folderName)
    {
        this.container  = container;
        this.folderName = folderName;
    }

    private string GetBlobFullName(string name) => $"{folderName}/{name}";

    /// <summary>
    /// List all existing blobs
    /// </summary>
    public virtual IAsyncEnumerable<TEntry> GetBlobEntriesAsync()
    {
        return container.GetBlobsAsync(prefix: $"{folderName}/").Select(bi => CreateEntry(bi));
    }
    protected virtual TEntry CreateEntry(BlobItem bi) => (TEntry)new BlobEntry(bi);

    /// <summary>
    /// Get an (existing or not existing) Blob
    /// Check whether the blob exists through the Exists property
    /// </summary>
    public Task<TBlob> GetBlobAsync(TEntry entry) => GetBlobAsync<TBlob>(entry);
    protected virtual Task<TBlob> GetBlobAsync<T>(TEntry entry)
    {
        var p = new Blob.Properties(entry);
        return Task.FromResult((TBlob)new Blob(container.GetBlockBlobClient(entry.FullName), p));
    }

    /// <summary>
    /// Get an (existing or not existing) Blob
    /// Check whether the blob exists through the Exists property
    /// </summary>
    public async Task<TBlob> GetBlobAsync(string name) => await GetBlobAsync<TBlob>(name);
    protected virtual async Task<TBlob> GetBlobAsync<T>(string name)
    {
        var bbc = container.GetBlockBlobClient(GetBlobFullName(name));

        try
        {
            var bp = await bbc.GetPropertiesAsync();
            var p  = new Blob.Properties(bp.Value);

            return CreateAzureBlob(bbc, p);
        }
        catch (RequestFailedException e) when (e.ErrorCode == "BlobNotFound")
        {
            // Blob does not exist
            var p = new Blob.Properties(exists: false);
            return CreateAzureBlob(bbc, p);
        }
    }

    protected virtual TBlob CreateAzureBlob(BlockBlobClient client, Blob.Properties properties) => (TBlob)new Blob(client, properties);

    public async Task<Response> DeleteBlobAsync(BlobEntry entry) => await container.DeleteBlobAsync(entry.FullName);
}

internal class ChunkBlobContainerFolder : BlobContainerFolder<ChunkBlobEntry, ChunkBlob>
{
    public ChunkBlobContainerFolder(BlobContainerClient containter, string folderName) : base(containter, folderName)
    {
    }

    protected override ChunkBlobEntry CreateEntry(BlobItem bi) => new ChunkBlobEntry(bi);

    public async Task<ChunkBlob> GetBlobAsync(ChunkHash chunkHash) => await base.GetBlobAsync<ChunkBlob>(chunkHash.Value);

    protected override ChunkBlob CreateAzureBlob(BlockBlobClient client, Blob.Properties properties) => new ChunkBlob(client, properties);
}