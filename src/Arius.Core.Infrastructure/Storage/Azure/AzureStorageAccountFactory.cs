using Arius.Core.Domain.Storage;

namespace Arius.Core.Infrastructure.Storage.Azure;

public class AzureStorageAccountFactory : IStorageAccountFactory
{
    public IStorageAccount Create(StorageAccountCredentials credentials)
    {
        return new AzureStorageAccount(credentials);
    }
}