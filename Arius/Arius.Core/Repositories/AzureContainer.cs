using Arius.Core.Models;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using PostSharp.Constraints;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Azure;

namespace Arius.Core.Repositories;

internal class BlobContainer
{
    internal const string STATE_DBS_FOLDER_NAME         = "states";
    internal const string CHUNK_LISTS_FOLDER_NAME       = "chunklists";
    internal const string CHUNKS_FOLDER_NAME            = "chunks";
    internal const string REHYDRATED_CHUNKS_FOLDER_NAME = "chunks-rehydrated";
    internal const char   BLOB_FOLDER_SEPARATOR_CHAR    = '/';

    private readonly BlobContainerClient container;

    public BlobContainer(BlobContainerClient container)
    {
        this.container = container;

        States           = new (container, STATE_DBS_FOLDER_NAME);
        ChunkLists       = new (container, CHUNK_LISTS_FOLDER_NAME);
        Chunks           = new (container, CHUNKS_FOLDER_NAME);
        RehydratedChunks = new (container, REHYDRATED_CHUNKS_FOLDER_NAME);
    }

    /// <summary>
    /// Create the container if it does not exist
    /// </summary>
    /// <returns>True if it was created. False if it already existed.</returns>
    public async Task<bool> CreateIfNotExistsAsync()
    {
        var r = await container.CreateIfNotExistsAsync(PublicAccessType.None);

        if (r is not null && r.GetRawResponse().Status == (int)HttpStatusCode.Created)
            return true;
        else
            return false;
    }

    public BlobContainerFolder<BlobEntry, Blob> States           { get; }
    public BlobContainerFolder<BlobEntry, Blob> ChunkLists       { get; }
    public ChunkBlobContainerFolder                       Chunks           { get; }
    public ChunkBlobContainerFolder                       RehydratedChunks { get; }
}

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
        var p = new BlobProperties(entry);
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
            var p  = new BlobProperties(bp.Value);

            return CreateAzureBlob(bbc, p);
        }
        catch (RequestFailedException e) when (e.ErrorCode == "BlobNotFound")
        {
            // Blob does not exist
            var p = new BlobProperties(exists: false);
            return CreateAzureBlob(bbc, p);
        }
    }

    protected virtual TBlob CreateAzureBlob(BlockBlobClient client, BlobProperties properties) => (TBlob)new Blob(client, properties);

    public async Task<Azure.Response> DeleteBlobAsync(BlobEntry entry) => await container.DeleteBlobAsync(entry.FullName);
}

record BlobProperties
{
    public BlobProperties(BlobEntry be)
    {
        this.Length        = be.Length;
        this.ContentType   = be.ContentType;
        this.AccessTier    = be.AccessTier;
        this.ContentType   = be.ContentType;
        this.ArchiveStatus = be.ArchiveStatus;
    }
    public BlobProperties(BlobItemProperties bip)
    {
        this.Length        = bip.ContentLength;
        this.ContentType   = bip.ContentType;
        this.AccessTier    = bip.AccessTier;
        this.Exists        = true;
        this.ArchiveStatus = bip.ArchiveStatus.ToString();
    }
    public BlobProperties(Azure.Storage.Blobs.Models.BlobProperties bp)
    {
        this.Length        = bp.ContentLength;
        this.ContentType   = bp.ContentType;
        this.AccessTier    = bp.AccessTier;
        this.Exists        = true;
        this.ArchiveStatus = bp.ArchiveStatus;
    }
    public BlobProperties(bool exists = false)
    {
        this.Exists = exists;
    }
    public long?       Length        { get; }
    public string?     ContentType   { get; set; }
    public AccessTier? AccessTier    { get; set; }
    public bool        Exists        { get; }
    public string?     ArchiveStatus { get; }
}



internal record BlobEntry
{
    private readonly BlobProperties properties;

    public BlobEntry(BlobItem item)
    {
        FullName   = item.Name;
        properties = new BlobProperties(item.Properties);
    }

    /// <summary>
    /// Full Name (with path and extension)
    /// </summary>
    public string FullName { get; }

    /// <summary>
    /// Name (with extension, without path)
    /// </summary>
    public string Name => FullName.Split(BlobContainer.BLOB_FOLDER_SEPARATOR_CHAR).Last(); //TODO werkt dit met alle soorten repos?

    /// <summary>
    /// The Folder where this Blob resides
    /// </summary>
    public string Folder => FullName.Split(BlobContainer.BLOB_FOLDER_SEPARATOR_CHAR).First(); //TODO quid if in the root?

    public long?       Length        => properties.Length;
    public string?     ContentType   => properties.ContentType;
    public AccessTier? AccessTier    => properties.AccessTier;
    public bool        Exists        => properties.Exists;
    public string?     ArchiveStatus => properties.ArchiveStatus;


    // METHODS
    public override string ToString() => FullName;
}

internal class Blob
{
    protected readonly BlockBlobClient client;
    private readonly   BlobProperties      properties;

    [ComponentInternal(typeof(BlobContainerFolder<,>))]
    public Blob(BlockBlobClient client, BlobProperties initialProperties)
    {
        this.client     = client;
        this.properties = initialProperties;
        this.FullName   = client.Name;
    }

    /// <summary>
    /// Full Name (with path and extension)
    /// </summary>
    public string FullName { get; }

    /// <summary>
    /// Name (with extension, without path)
    /// </summary>
    public string Name => FullName.Split(BlobContainer.BLOB_FOLDER_SEPARATOR_CHAR).Last(); //TODO werkt dit met alle soorten repos?

    /// <summary>
    /// The Folder where this Blob resides
    /// </summary>
    public string Folder => FullName.Split(BlobContainer.BLOB_FOLDER_SEPARATOR_CHAR).First(); //TODO quid if in the root?


    public async Task<Stream> OpenReadAsync() => await client.OpenReadAsync();
    public async Task<Stream> OpenWriteAsync(bool overwrite) => await client.OpenWriteAsync(overwrite);
    public async Task<Stream> OpenWriteAsync(bool overwrite, BlockBlobOpenWriteOptions options) => await client.OpenWriteAsync(overwrite, options);


    public AccessTier? AccessTier => properties.AccessTier;

    /// <summary>
    /// Sets the AccessTier of the Blob
    /// </summary>
    /// <returns>The tier has been updated</returns>
    public async Task<bool> SetAccessTierAsync(AccessTier accessTier)
    {
        if (accessTier == AccessTier)
            return false; // already in this access tier

        await client.SetAccessTierAsync(accessTier);

        properties.AccessTier = accessTier;

        return true;

        //TODO Unit test this: smaller blocks are not put into archive tier
    }

    //public bool IsDownloadable => Exists && AccessTier != Azure.Storage.Blobs.Models.AccessTier.Archive;
    public bool Hydrated => AccessTier != Azure.Storage.Blobs.Models.AccessTier.Archive;



    public string? ContentType => properties.ContentType;
    public async Task<Azure.Response<BlobInfo>> SetContentTypeAsync(string contentType)
    {
        var r = await client.SetHttpHeadersAsync(new BlobHttpHeaders() { ContentType = contentType });
        properties.ContentType = contentType;
        return r;
    }


    public long Length => properties.Length ?? 0;


    public bool Exists => properties.Exists;

    public async Task<Azure.Response> DeleteAsync()
    {
        return await client.DeleteAsync();
    }

    public async Task<CopyFromUriOperation> StartCopyFromUriAsync(Uri source, BlobCopyFromUriOptions options)
    {
        return await client.StartCopyFromUriAsync(source, options);
    }

    public bool HydrationPending
    {
        get
        {
            if (properties.ArchiveStatus is null)
                return false;
            else if (properties.ArchiveStatus.StartsWith("rehydrate-pending-to-", StringComparison.InvariantCultureIgnoreCase))
                return true;
            else
                throw new InvalidOperationException($"Unknown ArchiveStatus {properties.ArchiveStatus}");
        }
    }

    public Uri Uri => client.Uri;

    public override string ToString()
    {
        return FullName;
    }
}

internal class ChunkBlobContainerFolder : BlobContainerFolder<ChunkBlobEntry, ChunkBlob>
{
    public ChunkBlobContainerFolder(BlobContainerClient containter, string folderName) : base(containter, folderName)
    {
    }

    protected override ChunkBlobEntry CreateEntry(BlobItem bi) => new ChunkBlobEntry(bi);

    public          async Task<ChunkBlob> GetBlobAsync(ChunkHash chunkHash) => await base.GetBlobAsync<ChunkBlob>(chunkHash.Value);

    protected override ChunkBlob CreateAzureBlob(BlockBlobClient client, BlobProperties properties) => new ChunkBlob(client, properties);


    
}

internal record ChunkBlobEntry : BlobEntry
{
    public ChunkBlobEntry(BlobItem item) : base(item)
    {
    }

    public ChunkHash ChunkHash => new(Name);
}

internal class ChunkBlob : Blob, IChunk
{
    public ChunkBlob(BlockBlobClient client, BlobProperties initialProperties) : base(client, initialProperties)
    {
    }

    internal static AccessTier GetPolicyAccessTier(AccessTier targetAccessTier, long length)
    {
        const long oneMegaByte = 1024 * 1024; // TODO Derive this from the IArchiteCommandOptions?

        if (targetAccessTier == Azure.Storage.Blobs.Models.AccessTier.Archive &&
            length <= oneMegaByte)
        {
            return Azure.Storage.Blobs.Models.AccessTier.Cold; //Bringing back small files from archive storage is REALLY expensive. Only after 5.5 years, it is cheaper to store 1M in Archive
        }

        return targetAccessTier;
    }

    public ChunkHash ChunkHash => new(Name);
}