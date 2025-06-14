using Arius.Core.Models;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using System.Net;

namespace Arius.Core.Services;

internal class BlobStorage
{
    private readonly string              connectionString;
    private readonly BlobServiceClient   blobServiceClient;
    private readonly BlobContainerClient blobContainerClient;

    public BlobStorage(string accountName, string accountKey, string containerName)
    {
        connectionString    = $"DefaultEndpointsProtocol=https;AccountName={accountName};AccountKey={accountKey};EndpointSuffix=core.windows.net";
        blobServiceClient   = new BlobServiceClient(connectionString);
        blobContainerClient = blobServiceClient.GetBlobContainerClient(containerName);
    }

    /// <summary>
    /// Create Blob Container if it does not exist
    /// </summary>
    /// <returns>True if it was created</returns>
    public async Task<bool> CreateContainerIfNotExistsAsync()
    {
        var r = await blobContainerClient.CreateIfNotExistsAsync(PublicAccessType.None);

        return r is not null && r.GetRawResponse().Status == (int)HttpStatusCode.Created;
    }

    // --- STATES

    /// <summary>
    /// Get an ordered list of state names in the specified container.
    /// </summary>
    /// <returns></returns>
    public IAsyncEnumerable<string> GetStates(CancellationToken cancellationToken = default)
    {
        const string prefix = "states/";

        return blobContainerClient.GetBlobsAsync(prefix: prefix, cancellationToken: cancellationToken)
            .OrderBy(b => b.Name)
            .Select(b => b.Name[prefix.Length ..]); // remove the "states/" prefix
    }

    public async Task<Stream> OpenReadStateAsync(string stateName, CancellationToken cancellationToken = default)
    {
        var bbc = blobContainerClient.GetBlockBlobClient($"states/{stateName}");

        return await bbc.OpenReadAsync(cancellationToken: cancellationToken);
    }

    // --- CHUNKS

    public async Task<Stream> OpenReadChunkAsync(Hash h, CancellationToken cancellationToken = default)
    {
        var bbc = blobContainerClient.GetBlockBlobClient($"chunks/{h}");

        return await bbc.OpenReadAsync(cancellationToken: cancellationToken);
    }

    public async Task<Stream> OpenWriteChunkAsync(Hash h, string contentType, IDictionary<string, string> metadata = default, IProgress<long> progress = default, CancellationToken cancellationToken = default)
    {
        var bbc = blobContainerClient.GetBlockBlobClient($"chunks/{h}");

        var bbowo = new BlockBlobOpenWriteOptions();

        //NOTE the SDK only supports OpenWriteAsync with overwrite: true, therefore the ThrowOnExistOptions workaround
        var throwOnExists = false;
        if (throwOnExists)
            bbowo.OpenConditions = new BlobRequestConditions { IfNoneMatch = new ETag("*") }; // as per https://github.com/Azure/azure-sdk-for-net/issues/24831#issue-1031369473
        if (metadata is not null)
            bbowo.Metadata = metadata;
        bbowo.HttpHeaders     = new BlobHttpHeaders { ContentType = contentType };
        bbowo.ProgressHandler = progress;

        return await bbc.OpenWriteAsync(overwrite: true, options: bbowo, cancellationToken: cancellationToken);
    }

    public async Task<StorageTier> SetChunkStorageTierPerPolicy(Hash h, long length, StorageTier targetTier)
    {
        var actualTier = GetActualStorageTier(targetTier, length);
        var bbc        = blobContainerClient.GetBlobClient($"chunks/{h}");

        await bbc.SetAccessTierAsync(actualTier.ToAccessTier());

        return actualTier;
    }

    private static StorageTier GetActualStorageTier(StorageTier targetTier, long length)
    {
        const long oneMegaByte = 1024 * 1024; // TODO Derive this from the IArchiteCommandOptions?

        if (targetTier == StorageTier.Archive && length <= oneMegaByte)
                targetTier = StorageTier.Cold; //Bringing back small files from archive storage is REALLY expensive. Only after 5.5 years, it is cheaper to store 1M in Archive

        return targetTier;
    }
}