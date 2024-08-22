namespace Arius.Core.Domain.Storage;

public interface IStorageAccountFactory
{
    IStorageAccount GetStorageAccount(StorageAccountOptions storageAccountOptions);
    IStorageAccount GetStorageAccount(StorageAccountOptions storageAccountOptions, int maxRetries, TimeSpan timeout);
}