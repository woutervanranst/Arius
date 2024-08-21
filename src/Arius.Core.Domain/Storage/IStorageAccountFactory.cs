namespace Arius.Core.Domain.Storage;

public interface IStorageAccountFactory
{
    IStorageAccount Create(StorageAccountCredentials credentials);
    IStorageAccount Create(StorageAccountCredentials credentials, int maxRetries, TimeSpan timeout);
}