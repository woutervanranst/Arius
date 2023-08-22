using Arius.Core.Models;
using Azure;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Nito.AsyncEx;
using PostSharp.Constraints;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Arius.Core.Repositories.BlobRepository;


[ComponentInternal(typeof(Repository),      // only the Repository should be able to access these low level methods
    typeof(StateContainerFolder), typeof(BlobContainerFolder<>), typeof(ChunkBlobContainerFolder), typeof(ChunkListBlobContainerFolder), // and the folders
    typeof(ChunkBlob), typeof(ChunkListBlob),               // and the inherited classes
    typeof(RepositoryBuilder))]
internal class Blob
{
    protected readonly BlockBlobClient            client;
    private readonly   AsyncLazy<BlobProperties?> properties;

    [ComponentInternal(typeof(BlobContainerFolder<>), typeof(ChunkBlob), typeof(ChunkListBlob))]
    public Blob(BlockBlobClient client)
    {
        this.client     = client;
        FullName   = client.Name;

        properties = new AsyncLazy<BlobProperties?>(async () =>
        {
            try
            {
                return await client.GetPropertiesAsync();
            }
            catch (RequestFailedException e) when (e.ErrorCode == "BlobNotFound")
            {
                // Blob does not exist
                return null;
            }
        });
    }

    /// <summary>
    /// Full Name (with path and extension)
    /// </summary>
    public string FullName { get; }

    /// <summary>
    /// Name (with extension, without path)
    /// </summary>
    public string Name => Path.GetFileName(FullName);

    /// <summary>
    /// The Folder where this Blob resides. If the folder is in the root, returns an empty string.
    /// </summary>
    public string Folder => Path.GetDirectoryName(FullName) ?? string.Empty;

    public async Task<Stream> OpenReadAsync() => await client.OpenReadAsync();

    /// <summary>
    /// Open the blob for writing.
    /// </summary>
    /// <param name="throwOnExists">If specified, and the blob already exists, a RequestFailedException with Status HttpStatusCode.Conflict is thrown</param>
    public async Task<Stream> OpenWriteAsync(bool throwOnExists = true)
    {
        //NOTE the SDK only supports OpenWriteAsync with overwrite: true, therefore the ThrowOnExistOptions workaround

        if (throwOnExists) 
            return await client.OpenWriteAsync(overwrite: true, options: new BlockBlobOpenWriteOptions()
            {
                OpenConditions = new BlobRequestConditions() { IfNoneMatch = new ETag("*")} // as per https://github.com/Azure/azure-sdk-for-net/issues/24831#issue-1031369473
            } );
        else
            return await client.OpenWriteAsync(overwrite: true);
    }


    public async Task<AccessTier> GetAccessTierAsync()                      => (await properties.Task)!.AccessTier; // NOTE Properties can be null in case of a non-existing blob but that should not happen here
    public async Task             SetAccessTierAsync(AccessTier accessTier) => await client.SetAccessTierAsync(accessTier);


    private string? contentTypeOverride = null;
    public async Task<string> GetContentType() => contentTypeOverride ?? (await properties.Task)!.ContentType; // NOTE Properties can be null in case of a non-existing blob but that should not happen here
    public async Task SetContentTypeAsync(string contentType)
    {
        await client.SetHttpHeadersAsync(new BlobHttpHeaders() { ContentType = contentType });
        contentTypeOverride = contentType;
    }

    
    private long? originalLengthMetadataOverride = null;
    public async Task<long?> GetOriginalLengthMetadata()
    {
        if (originalLengthMetadataOverride is not null)
            return originalLengthMetadataOverride;

        var p = await properties.Task;
        if (p is null)
            return null;
        if (p.Metadata.TryGetValue(ORIGINAL_CONTENT_LENGTH_METADATA_KEY, out string? value))
            return long.Parse(value);
        else
            return null;
    }
    public async Task SetOriginalLengthMetadata(long length)
    {
        var m = (await properties.Task).Metadata;

        m.Add(ORIGINAL_CONTENT_LENGTH_METADATA_KEY, length.ToString());

        await client.SetMetadataAsync(m);

        originalLengthMetadataOverride = length;
    }
    public const string ORIGINAL_CONTENT_LENGTH_METADATA_KEY = "OriginalContentLength";


    public async Task<long?> GetArchivedLength() => (await properties.Task)?.ContentLength;

    public async Task<bool> ExistsAsync()
    {
        var p = (await properties.Task);
        return p is not null;
    }

    public async Task DeleteAsync() => await client.DeleteAsync();

    public async Task<CopyFromUriOperation> StartCopyFromUriAsync(Uri source, BlobCopyFromUriOptions options) => await client.StartCopyFromUriAsync(source, options);

    public async Task<bool> IsHydrationPendingAsync()
    {
        var p = await properties.Task;
        if (p is null)
            throw new InvalidOperationException("This blob does not exist");

        if (p.ArchiveStatus is null)
            return false;
        else if (p.ArchiveStatus.StartsWith("rehydrate-pending-to-", StringComparison.InvariantCultureIgnoreCase))
            return true;
        else
            throw new InvalidOperationException($"Unknown ArchiveStatus {p.ArchiveStatus}");
    }

    public Uri Uri => client.Uri;

    public override string ToString() => FullName;
}

internal class ChunkBlob : Blob, IChunk
{
    public ChunkBlob(BlockBlobClient client) : base(client)
    {
        ChunkHash = new ChunkHash(Name.HexStringToBytes());
    }

    internal static AccessTier GetPolicyAccessTier(AccessTier targetAccessTier, long length)
    {
        const long oneMegaByte = 1024 * 1024; // TODO Derive this from the IArchiteCommandOptions?

        if (targetAccessTier == AccessTier.Archive &&
            length <= oneMegaByte)
        {
            return AccessTier.Cold; //Bringing back small files from archive storage is REALLY expensive. Only after 5.5 years, it is cheaper to store 1M in Archive
        }

        //TODO Unit test this: smaller blocks are not put into archive tier


        return targetAccessTier;
    }

    public ChunkHash ChunkHash { get; }
}

internal class ChunkListBlob : Blob
{
    public ChunkListBlob(BlockBlobClient client) : base(client)
    {
        BinaryHash = new BinaryHash(Name.HexStringToBytes());
    }

    public BinaryHash BinaryHash { get; }
}