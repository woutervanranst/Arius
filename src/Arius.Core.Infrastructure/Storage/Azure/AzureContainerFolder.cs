using System.Net;
using Arius.Core.Domain.Services;
using Arius.Core.Domain.Storage;
using Arius.Core.Domain.Storage.FileSystem;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Specialized;

namespace Arius.Core.Infrastructure.Storage.Azure;

internal class AzureContainerFolder
{
    private readonly BlobContainerClient          blobContainerClient;
    private readonly RemoteRepositoryOptions      remoteRepositoryOptions;
    private readonly string                       folderName;
    private readonly ICryptoService               cryptoService;
    private readonly ILogger logger;

    public AzureContainerFolder(
        BlobContainerClient blobContainerClient,
        RemoteRepositoryOptions remoteRepositoryOptions,
        string folderName,
        ICryptoService cryptoService,
        ILogger logger)
    {
        this.blobContainerClient     = blobContainerClient;
        this.remoteRepositoryOptions = remoteRepositoryOptions;
        this.folderName              = folderName;
        this.cryptoService           = cryptoService;
        this.logger                  = logger;
    }

    public IAsyncEnumerable<AzureBlob> GetBlobs()
    {
        return blobContainerClient
            .GetBlobsAsync(prefix: $"{folderName}/")
            .Select(bi => new AzureBlob(bi, blobContainerClient));
    }

    public AzureBlob GetBlob(string name)
    {
        return new AzureBlob(blobContainerClient.GetBlockBlobClient($"{folderName}/{name}"));
    }

    public async Task<(long originalLength, long archivedLength)> UploadAsync(IFile source, AzureBlob target, IDictionary<string, string> metadata, CancellationToken cancellationToken = default)
    {
        ValidateBlobBelongsToFolder(target);

        RestartUpload:

        try
        {
            await using var ss = source.OpenRead();
            await using var ts = await target.OpenWriteAsync(
                contentType: ICryptoService.ContentType,
                metadata: metadata,
                throwOnExists: true,
                cancellationToken: cancellationToken);
            await cryptoService.CompressAndEncryptAsync(ss, ts, remoteRepositoryOptions.Passphrase);

            return (ss.Length, ts.Position); // ts.Length is not supported
        }
        catch (RequestFailedException rfe) when (rfe.Status == (int)HttpStatusCode.Conflict)
        {
            // The blob already exists
            if (await target.GetContentTypeAsync() != ICryptoService.ContentType || await target.GetContentLengthAsync() == 0)
            {
                logger.LogWarning($"Corrupt Binary {target.FullName}. Deleting and uploading again");
                await target.DeleteAsync();

                goto RestartUpload;
            }
            else
            {
                // graceful handling if the chunk is already uploaded but it does not yet exist in the database
                logger.LogWarning($"A valid Binary '{target.FullName}' already existed, perhaps from a previous/crashed run?");

                return (await target.GetOriginalContentLengthAsync() ?? 0, await target.GetContentLengthAsync());
            }
        }
    }

    public async Task DownloadAsync(IBlob blob, IFile file, CancellationToken cancellationToken = default)
    {
        ValidateBlobBelongsToFolder(blob);

        if (blob is AzureBlob azureBlob)
            await DownloadAsync(azureBlob, file, cancellationToken);
        else
            throw new NotSupportedException($"'{blob.GetType()}' is not supported");
    }

    private async Task DownloadAsync(AzureBlob blob, IFile file, CancellationToken cancellationToken = default)
    {
        await using var ss = await blob.OpenReadAsync(cancellationToken);
        await using var ts = file.OpenWrite();
        await cryptoService.DecryptAndDecompressAsync(ss, ts, remoteRepositoryOptions.Passphrase);

        logger.LogInformation("Successfully downloaded latest state '{blob}' to '{file}'", blob.Name, file);
    }

    private void ValidateBlobBelongsToFolder(IBlob blob)
    {
        if (!blob.FullName.StartsWith(folderName))
            throw new ArgumentException("TODO DOES NOT CONTAIN THIS FILE");
    }
}