using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Arius.Core.Models;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using PostSharp.Constraints;

namespace Arius.Core.Repositories.BlobRepository;

internal class Blob
{
    protected readonly BlockBlobClient client;
    private readonly   Properties  properties;

    [ComponentInternal(typeof(BlobContainerFolder<,>))]
    public Blob(BlockBlobClient client, Properties initialProperties)
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


    public async Task<Stream> OpenReadAsync()                                                   => await client.OpenReadAsync();

    public async Task<Stream> OpenWriteAsync(bool overwrite)                                    => await client.OpenWriteAsync(overwrite);
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

    public record Properties // NOTE this can be an origin of nasty side effects, eg. Exists and Length change after upload
    {
        public Properties(BlobEntry be)
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
        public string?     ContentType   { get; set; }
        public AccessTier? AccessTier    { get; set; }
        public bool        Exists        { get; }
        public string?     ArchiveStatus { get; }
    }
}

internal class ChunkBlob : Blob, IChunk
{
    public ChunkBlob(BlockBlobClient client, Properties initialProperties) : base(client, initialProperties)
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