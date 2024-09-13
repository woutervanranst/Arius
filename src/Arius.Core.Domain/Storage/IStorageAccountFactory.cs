namespace Arius.Core.Domain.Storage;

public interface IStorageAccountFactory
{
    IStorageAccount GetStorageAccount(StorageAccountOptions storageAccountOptions);
    IStorageAccount GetStorageAccount(StorageAccountOptions storageAccountOptions, int maxRetries, TimeSpan timeout);

    public IContainer GetContainer(ContainerOptions containerOptions)
    {
        return GetStorageAccount(containerOptions)
            .GetContainer(containerOptions.ContainerName);
    }

    public IRemoteRepository GetRemoteRepository(RemoteRepositoryOptions remoteRepositoryOptions)
    {
        return GetStorageAccount(remoteRepositoryOptions)
            .GetContainer(remoteRepositoryOptions.ContainerName)
            .GetRemoteRepository(remoteRepositoryOptions.Passphrase);
    }
}