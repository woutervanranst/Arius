using Arius.Core.Extensions;
using Arius.Core.Models;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using System.Collections.Concurrent;
using System.Net;

namespace Arius.Core.Services;

internal class BlobStorage
{
    private readonly string            connectionString;
    private readonly BlobServiceClient blobServiceClient;

    private const string ChunkContentType = "application/aes256cbc+gzip";

    public BlobStorage(string accountName, string accountKey)
    {
        connectionString = $"DefaultEndpointsProtocol=https;AccountName={accountName};AccountKey={accountKey};EndpointSuffix=core.windows.net";
        blobServiceClient = new BlobServiceClient(connectionString);
    }

    private BlobContainerClient GetBlobContainerClient(string containerName)
    {
        return containerClients.GetOrAdd(containerName, _ => blobServiceClient.GetBlobContainerClient(containerName));
    }
    private readonly ConcurrentDictionary<string, BlobContainerClient> containerClients = new();

    /// <summary>
    /// Create Blob Container if it does not exist
    /// </summary>
    /// <param name="containerName"></param>
    /// <returns>True if it was created</returns>
    public async Task<bool> CreateContainerIfNotExistsAsync(string containerName)
    {
        var bcc = GetBlobContainerClient(containerName);
        
        var r = await bcc.CreateIfNotExistsAsync(PublicAccessType.None);

        return r is not null && r.GetRawResponse().Status == (int)HttpStatusCode.Created;
    }

    //public async Task<Stream> OpenChunkWriteAsync(string containerName, Hash h, IDictionary<string, string>? metadata = default, IProgress<long> progress = default)
    //{
    //    var bbc = new BlockBlobClient(connectionString, containerName, $"chunks/{h}");

    //    var bbowo = new BlockBlobOpenWriteOptions();

    //    //NOTE the SDK only supports OpenWriteAsync with overwrite: true, therefore the ThrowOnExistOptions workaround
    //    var throwOnExists = false;
    //    if (throwOnExists)
    //        bbowo.OpenConditions = new BlobRequestConditions { IfNoneMatch = new ETag("*") }; // as per https://github.com/Azure/azure-sdk-for-net/issues/24831#issue-1031369473
    //    if (metadata is not null)
    //        bbowo.Metadata = metadata;
    //    bbowo.HttpHeaders     = new BlobHttpHeaders { ContentType = ChunkContentType };
    //    bbowo.ProgressHandler = progress;

    //    return await bbc.OpenWriteAsync(overwrite: true, options: bbowo);
    //}

    public async Task<(StorageTier ActualTier, long ArchivedSize)> UploadAsync(Stream source, string containerName, Hash h, string passphrase, StorageTier targetTier, IDictionary<string, string> metadata = default, IProgress<long> progress = default, CancellationToken cancellationToken = default)
    {
        var bbc = new BlockBlobClient(connectionString, containerName, $"chunks/{h}");

        var bbowo = new BlockBlobOpenWriteOptions();

        //NOTE the SDK only supports OpenWriteAsync with overwrite: true, therefore the ThrowOnExistOptions workaround
        var throwOnExists = false;
        if (throwOnExists)
            bbowo.OpenConditions = new BlobRequestConditions { IfNoneMatch = new ETag("*") }; // as per https://github.com/Azure/azure-sdk-for-net/issues/24831#issue-1031369473
        if (metadata is not null)
            bbowo.Metadata = metadata;
        bbowo.HttpHeaders     = new BlobHttpHeaders { ContentType = ChunkContentType };
        bbowo.ProgressHandler = progress;

        await using var ts = await bbc.OpenWriteAsync(overwrite: true, options: bbowo, cancellationToken: cancellationToken);

        await source.CopyToCompressedEncryptedAsync(ts, passphrase, cancellationToken: cancellationToken);

        var actualTier = GetActualStorageTier(targetTier, ts.Position);

        var r = await bbc.SetAccessTierAsync(targetTier.ToAccessTier());

        return (actualTier, ts.Position);
    }

    private static StorageTier GetActualStorageTier( StorageTier targetTier, long length)
    {
        const long oneMegaByte = 1024 * 1024; // TODO Derive this from the IArchiteCommandOptions?

        if (targetTier == StorageTier.Archive && length <= oneMegaByte)
                targetTier = StorageTier.Cold; //Bringing back small files from archive storage is REALLY expensive. Only after 5.5 years, it is cheaper to store 1M in Archive

        return targetTier;
    }
}