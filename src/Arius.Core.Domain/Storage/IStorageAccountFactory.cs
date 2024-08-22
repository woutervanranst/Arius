namespace Arius.Core.Domain.Storage;

public interface IStorageAccountFactory
{
    IStorageAccount Create(StorageAccountOptions storageAccountOptions);
    IStorageAccount Create(StorageAccountOptions storageAccountOptions, int maxRetries, TimeSpan timeout);
}