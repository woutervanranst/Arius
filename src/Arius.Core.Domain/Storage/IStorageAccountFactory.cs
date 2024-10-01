using FluentValidation;

namespace Arius.Core.Domain.Storage;

public interface IStorageAccountFactory
{
    IStorageAccount GetStorageAccount(StorageAccountOptions storageAccountOptions);
    IStorageAccount GetStorageAccount(StorageAccountOptions storageAccountOptions, int maxRetries, TimeSpan timeout);

    public sealed IContainer GetContainer(ContainerOptions containerOptions)
    {
        new ContainerOptionsValidator().ValidateAndThrow(containerOptions);

        return GetStorageAccount(containerOptions)
            .GetContainer(containerOptions);
    }
}

public static class IStorageAccountFactoryExtensions
{
    public static IRemoteRepository GetRemoteRepository(this IStorageAccountFactory storageAccountFactory, RemoteRepositoryOptions remoteRepositoryOptions)
    {
        new RepositoryOptionsValidator().ValidateAndThrow(remoteRepositoryOptions);

        return storageAccountFactory
            .GetStorageAccount(remoteRepositoryOptions)
            .GetContainer(remoteRepositoryOptions)
            .GetRemoteRepository(remoteRepositoryOptions);
    }
}