namespace Arius.Core.Domain.Storage;

public interface IStorageAccountFactory
{
    IStorageAccount Create(StorageAccountCredentials credentials);
}