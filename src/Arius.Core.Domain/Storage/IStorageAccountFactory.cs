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

    public ICloudRepository GetCloudRepository(RepositoryOptions repositoryOptions)
    {
        return GetStorageAccount(repositoryOptions)
            .GetContainer(repositoryOptions.ContainerName)
            .GetCloudRepository(repositoryOptions.Passphrase);
    }
}