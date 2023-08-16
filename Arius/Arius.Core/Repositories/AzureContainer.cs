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

internal class AzureContainer
{
    internal const string STATE_DBS_FOLDER_NAME         = "states";
    internal const string CHUNK_LISTS_FOLDER_NAME       = "chunklists";
    internal const string CHUNKS_FOLDER_NAME            = "chunks";
    internal const string REHYDRATED_CHUNKS_FOLDER_NAME = "chunks-rehydrated";
    internal const char   BLOB_FOLDER_SEPARATOR_CHAR    = '/';

    private readonly BlobContainerClient container;

    public AzureContainer(BlobContainerClient container)
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

    public AzureContainerFolder<AzureBlobEntry, AzureBlob> States           { get; }
    public AzureContainerFolder<AzureBlobEntry, AzureBlob> ChunkLists       { get; }
    public AzureChunkContainerFolder                       Chunks           { get; }
    public AzureChunkContainerFolder                       RehydratedChunks { get; }
}

internal class AzureContainerFolder<TEntry, TBlob> where TEntry : AzureBlobEntry where TBlob : AzureBlob
{
    protected readonly BlobContainerClient container;
    protected readonly string              folderName;

    public AzureContainerFolder(BlobContainerClient container, string folderName)
    {
        this.container  = container;
        this.folderName = folderName;
    }

    protected string GetBlobFullName(string name) => $"{folderName}/{name}";

    /// <summary>
    /// List all existing blobs
    /// </summary>
    public virtual IAsyncEnumerable<TEntry> GetBlobEntriesAsync()
    {
        return container.GetBlobsAsync(prefix: GetBlobFullName("")).Select(bi => (TEntry)new AzureBlobEntry(bi));
    }

    /// <summary>
    /// Get an (existing or not existing) Blob
    /// Check whether the blob exists through the Exists property
    /// </summary>
    public virtual async Task<TBlob> GetBlobAsync<T>(TEntry entry)
    {
        var p = new Properties(entry);
        return await Task.FromResult((TBlob)new AzureBlob(container.GetBlockBlobClient(entry.FullName), p));
    }

    /// <summary>
    /// Get an (existing or not existing) Blob
    /// Check whether the blob exists through the Exists property
    /// </summary>
    public virtual async Task<TBlob> GetBlobAsync<T>(string name)
    {
        var bbc = container.GetBlockBlobClient(GetBlobFullName(name));

        try
        {
            var bp = await bbc.GetPropertiesAsync();
            var p  = new Properties(bp.Value);

            return (TBlob)CreateAzureBlob(bbc, p);
        }
        catch (RequestFailedException e) when (e.ErrorCode == "BlobNotFound")
        {
            // Blob does not exist
            var p = new Properties(exists: false);
            return (TBlob)CreateAzureBlob(bbc, p);
        }
    }

    //protected virtual T CreateAzureBlob<T>(BlockBlobClient client, Properties properties) where T : AzureBlob
    //{
    //    return (T)new AzureBlob(client, properties);
    //}
    protected virtual AzureBlob CreateAzureBlob(BlockBlobClient client, Properties properties)
    {
        return new AzureBlob(client, properties);
    }



    ///// <summary>
    ///// Get an existing blob. If it does not exist, returns null
    ///// </summary>
    //public virtual async Task<TBlob?> GetExistingBlobAsync(TEntry entry) => await GetBlobAsync<TBlob>(entry); // this can point to GetBlobAsync since if it has a BlobItem it exists (as it originates from the LIST operation)
    //public virtual async Task<TBlob?> GetExistingBlobAsync(string name) => await GetExistingBlobAsync<TBlob>(name);
    //protected async Task<T?> GetExistingBlobAsync<T>(string name) where T : TBlob
    //{
    //    var b = await GetBlobAsync<T>(name);
    //    if (b.Exists)
    //        return b;
    //    else
    //        return null;
    //}

    public async Task<Azure.Response> DeleteBlobAsync(AzureBlobEntry entry) => await container.DeleteBlobAsync(entry.FullName);
}

record Properties
{
    public Properties(AzureBlobEntry be)
    {
        this.Length        = be.Length;
        this.ContentType   = be.ContentType;
        this.AccessTier    = be.AccessTier;
        this.ContentType   = be.ContentType;
        this.ArchiveStatus = be.ArchiveStatus;
    }
    public Properties(BlobItemProperties bip)
    {
        this.Length        = bip.ContentLength;
        this.ContentType   = bip.ContentType;
        this.AccessTier    = bip.AccessTier;
        this.Exists        = true;
        this.ArchiveStatus = bip.ArchiveStatus.ToString();
    }
    public Properties(BlobProperties bp)
    {
        this.Length        = bp.ContentLength;
        this.ContentType   = bp.ContentType;
        this.AccessTier    = bp.AccessTier;
        this.Exists        = true;
        this.ArchiveStatus = bp.ArchiveStatus;
    }
    public Properties(bool exists = false)
    {
        this.Exists = exists;
    }
    public long?       Length        { get; }
    public string?     ContentType   { get; }
    public AccessTier? AccessTier    { get; }
    public bool        Exists        { get; }
    public string?     ArchiveStatus { get; }
}



internal record AzureBlobEntry
{
    private readonly Properties properties;

    public AzureBlobEntry(BlobItem item)
    {
        FullName   = item.Name;
        properties = new Properties(item.Properties);
    }

    /// <summary>
    /// Full Name (with path and extension)
    /// </summary>
    public string FullName { get; }

    /// <summary>
    /// Name (with extension, without path)
    /// </summary>
    public string Name => FullName.Split(AzureContainer.BLOB_FOLDER_SEPARATOR_CHAR).Last(); //TODO werkt dit met alle soorten repos?

    /// <summary>
    /// The Folder where this Blob resides
    /// </summary>
    public string Folder => FullName.Split(AzureContainer.BLOB_FOLDER_SEPARATOR_CHAR).First(); //TODO quid if in the root?

    public long?       Length        => properties.Length;
    public string?     ContentType   => properties.ContentType;
    public AccessTier? AccessTier    => properties.AccessTier;
    public bool        Exists        => properties.Exists;
    public string?     ArchiveStatus => properties.ArchiveStatus;


    // METHODS
    public override string ToString() => FullName;
}

internal class AzureBlob
{
    protected readonly BlockBlobClient client;
    private readonly   Properties      properties;

    [ComponentInternal(typeof(AzureContainerFolder<,>))]
    public AzureBlob(BlockBlobClient client, Properties initialProperties)
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
    public string Name => FullName.Split(AzureContainer.BLOB_FOLDER_SEPARATOR_CHAR).Last(); //TODO werkt dit met alle soorten repos?

    /// <summary>
    /// The Folder where this Blob resides
    /// </summary>
    public string Folder => FullName.Split(AzureContainer.BLOB_FOLDER_SEPARATOR_CHAR).First(); //TODO quid if in the root?


    public async Task<Stream> OpenReadAsync() => await client.OpenReadAsync();
    public async Task<Stream> OpenWriteAsync(bool overwrite) => await client.OpenWriteAsync(overwrite);
    public async Task<Stream> OpenWriteAsync(bool overwrite, BlockBlobOpenWriteOptions options) => await client.OpenWriteAsync(overwrite, options);


    public AccessTier? AccessTier => properties.AccessTier;
    public virtual async Task<bool> SetAccessTierAsync(AccessTier accessTier)
    {
        await client.SetAccessTierAsync(accessTier);
        return true;
    }

    //public bool IsDownloadable => Exists && AccessTier != Azure.Storage.Blobs.Models.AccessTier.Archive;
    public bool Hydrated => AccessTier != Azure.Storage.Blobs.Models.AccessTier.Archive;



    public string? ContentType => properties.ContentType;
    public async Task<Azure.Response<BlobInfo>> SetContentTypeAsync(string contentType) => await client.SetHttpHeadersAsync(new BlobHttpHeaders() { ContentType = contentType });


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

internal class AzureChunkContainerFolder : AzureContainerFolder<AzureChunkContainerFolder.AzureChunkBlobEntry, AzureChunkContainerFolder.AzureChunkBlob>
{
    public AzureChunkContainerFolder(BlobContainerClient containter, string folderName) : base(containter, folderName)
    {
    }

    public override IAsyncEnumerable<AzureChunkBlobEntry> GetBlobEntriesAsync()
    {
        return container.GetBlobsAsync(prefix: GetBlobFullName("")).Select(bi => new AzureChunkBlobEntry(bi));
    }

    public          async Task<AzureChunkBlob> GetBlobAsync(ChunkHash chunkHash) => await base.GetBlobAsync<AzureChunkBlob>(chunkHash.Value);

    protected override AzureBlob CreateAzureBlob(BlockBlobClient client, Properties properties)
    {
        return new AzureChunkBlob(client, properties);
    }
    //public override       Task<AzureChunkBlob> GetBlobAsync<T>(AzureChunkBlobEntry entry)
    //{
    //    return base.GetBlobAsync<T>(entry);
    //}

    //public override async Task<AzureChunkBlob> GetBlobAsync(string name)               => (AzureChunkBlob)await base.GetBlobAsync<AzureChunkBlob>(name);

    //protected override AzureBlob CreateAzureBlob<T>(BlockBlobClient client, Properties properties)
    //{
    //    return new AzureChunkBlob(client, properties);
    //}

    //public async          Task<AzureChunkBlob?> GetExistingBlobAsync(ChunkHash chunkHash) => await base.GetExistingBlobAsync(chunkHash.Value);
    //public override async Task<AzureChunkBlob?> GetExistingBlobAsync(string name)         => await base.GetExistingBlobAsync(name);


    internal record AzureChunkBlobEntry : AzureBlobEntry
    {
        public AzureChunkBlobEntry(BlobItem item) : base(item)
        {
        }

        public ChunkHash ChunkHash => new(Name);
    }

    internal class AzureChunkBlob : AzureBlob, IChunk
    {
        public AzureChunkBlob(BlockBlobClient client, Properties initialProperties) : base(client, initialProperties)
        {
        }

        /// <summary>
        /// Sets the AccessTier of the Blob according to the policy and the target access tier
        /// </summary>
        /// <returns>The tier has been updated</returns>
        public override async Task<bool> SetAccessTierAsync(AccessTier accessTier)
        {
            accessTier = GetPolicyAccessTier(accessTier, Length);

            if (AccessTier == accessTier)
                return false; // already in this Access Tier

            await client.SetAccessTierAsync(accessTier);
            return true;

            //TODO Unit test this: smaller blocks are not put into archive tier
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
}