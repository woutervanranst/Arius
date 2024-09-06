using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Specialized;

namespace Arius.Core.Infrastructure.Storage.Azure;

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
            .Select(bi => new AzureBlob(bi, blobContainerClient));
    }

    public AzureBlob GetBlob(string name)
    {
        return new AzureBlob(blobContainerClient.GetBlockBlobClient($"{folderName}/{name}"));
    }
}