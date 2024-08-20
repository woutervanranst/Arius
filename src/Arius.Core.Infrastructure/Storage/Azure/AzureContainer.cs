using Arius.Core.Domain.Storage;
using Azure.Storage.Blobs;

namespace Arius.Core.Infrastructure.Storage.Azure;

internal class AzureContainer : IContainer
{
    private readonly BlobContainerClient blobContainerClient;

    public AzureContainer(BlobContainerClient blobContainerClient)
    {
        this.blobContainerClient = blobContainerClient;
    }

    public string Name => blobContainerClient.Name;

    public async Task<IEnumerable<string>> ListBlobsAsync(CancellationToken cancellationToken = default)
    {
        var blobs = new List<string>();

        await foreach (var blobItem in blobContainerClient.GetBlobsAsync(cancellationToken: cancellationToken))
        {
            blobs.Add(blobItem.Name);
        }

        return blobs;
    }
}