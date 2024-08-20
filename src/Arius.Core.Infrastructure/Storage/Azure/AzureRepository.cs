using Arius.Core.Domain.Storage;
using Azure.Storage.Blobs;

namespace Arius.Core.Infrastructure.Storage.Azure;

internal class AzureRepository : IRepository
{
    private readonly BlobContainerClient blobContainerClient;
    private readonly string              passphrase;

    public AzureRepository(IContainer container, BlobContainerClient blobContainerClient, string passphrase)
    {
        this.blobContainerClient = blobContainerClient;
        this.passphrase          = passphrase;
        Container                = container;
    }
    public IContainer Container  { get; }

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