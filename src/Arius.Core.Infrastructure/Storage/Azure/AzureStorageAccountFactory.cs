using Arius.Core.Domain.Storage;
using Azure.Storage.Blobs;

namespace Arius.Core.Infrastructure.Storage.Azure;

public class AzureStorageAccountFactory : IStorageAccountFactory
{
    public IStorageAccount GetStorageAccount(StorageAccountOptions storageAccountOptions)
    {
        return new AzureStorageAccount(storageAccountOptions);
    }

    public IStorageAccount GetStorageAccount(StorageAccountOptions storageAccountOptions, int maxRetries, TimeSpan timeout)
    {
        var o = new BlobClientOptions
        {
            Retry =
            {
                MaxRetries     = maxRetries,
                NetworkTimeout = timeout
            }
        };

        return new AzureStorageAccount(storageAccountOptions, o);
    }
}