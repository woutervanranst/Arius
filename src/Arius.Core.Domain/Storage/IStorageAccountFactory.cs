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

    public ICloudRepository GetCloudRepository(CloudRepositoryOptions cloudRepositoryOptions)
    {
        return GetStorageAccount(cloudRepositoryOptions)
            .GetContainer(cloudRepositoryOptions.ContainerName)
            .GetCloudRepository(cloudRepositoryOptions.Passphrase);
    }
}