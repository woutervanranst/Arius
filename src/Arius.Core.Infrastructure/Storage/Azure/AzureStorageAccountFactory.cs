using Arius.Core.Domain.Storage;
using Azure.Storage.Blobs;

namespace Arius.Core.Infrastructure.Storage.Azure;

public class AzureStorageAccountFactory : IStorageAccountFactory
{
    public IStorageAccount Create(StorageAccountCredentials credentials)
    {
        return new AzureStorageAccount(credentials);
    }

    public IStorageAccount Create(StorageAccountCredentials credentials, int maxRetries, TimeSpan timeout)
    {
        var o = new BlobClientOptions
        {
            Retry =
            {
                MaxRetries     = maxRetries,
                NetworkTimeout = timeout
            }
        };

        return new AzureStorageAccount(credentials, o);
    }
}