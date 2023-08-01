using Arius.Core.Commands;
using Azure.Storage;
using Azure.Storage.Blobs;
using FluentValidation;
using System;

namespace Arius.Core.Facade;

internal interface IStorageAccountOptions
{
    string AccountName { get; }
    string AccountKey  { get; }
}

internal record StorageAccountOptions : IStorageAccountOptions
{
    public StorageAccountOptions(string accountName, string accountKey)
    {
        this.AccountName = accountName;
        this.AccountKey  = accountKey;
    }

    public string AccountName { get; }
    public string AccountKey  { get; }
}

internal static class StorageAccountOptionsExtensions
{
    public static BlobServiceClient GetBlobServiceClient(this IStorageAccountOptions storageAccount)
    {
        return new BlobServiceClient(new Uri($"https://{storageAccount.AccountName}.blob.core.windows.net/"), 
            new StorageSharedKeyCredential(storageAccount.AccountName, storageAccount.AccountKey));
    }
    public static BlobServiceClient GetBlobServiceClient(this IStorageAccountOptions storageAccount, BlobClientOptions options)
    {
        return new BlobServiceClient(new Uri($"https://{storageAccount.AccountName}.blob.core.windows.net/"),
            new StorageSharedKeyCredential(storageAccount.AccountName, storageAccount.AccountKey),
            options);
    }
}



//internal interface IContainerOptions : IStorageAccountOptions
//{
//    string ContainerName { get; }
//}

//internal record ContainerOptions : IContainerOptions
//{
//    public ContainerOptions(IStorageAccountOptions storageAccountOptions, string containerName)
//    {
//        this.AccountName   = storageAccountOptions.AccountName;
//        this.AccountKey    = storageAccountOptions.AccountKey;
//        this.ContainerName = containerName;
//    }

//    public string AccountName   { get; }
//    public string AccountKey    { get; }
//    public string ContainerName { get; }
//}






internal interface IRepositoryOptions : IStorageAccountOptions, ICommandOptions // TODO make interface INTERNAL // TODO remove ICommandOptions 
{
    string ContainerName { get; }
    string Passphrase    { get; }

#pragma warning disable CS0108 // Member hides inherited member; missing new keyword -- not required
    protected class Validator : AbstractValidator<IRepositoryOptions>
#pragma warning restore CS0108
    {
        public Validator()
        {
            RuleFor(o => o.AccountName).NotEmpty();
            RuleFor(o => o.AccountKey).NotEmpty();
            RuleFor(o => o.ContainerName).NotEmpty();
            RuleFor(o => o.Passphrase).NotEmpty();
        }
    }
}

internal record RepositoryOptions : IRepositoryOptions
{
    public RepositoryOptions(IRepositoryOptions repositoryOptions)
    {
        AccountName   = repositoryOptions.AccountName;
        AccountKey    = repositoryOptions.AccountKey;
        ContainerName = repositoryOptions.ContainerName;
        Passphrase    = repositoryOptions.Passphrase;
    }
    public RepositoryOptions(IStorageAccountOptions containerOptions, string containerName, string passphrase)
    {
        AccountName   = containerOptions.AccountName;
        AccountKey    = containerOptions.AccountKey;
        ContainerName = containerName;
        Passphrase    = passphrase;
    }

    public string AccountName   { get; }
    public string AccountKey    { get; }
    public string ContainerName { get; }
    public string Passphrase    { get; }
}

internal static class ContainerOptionsExtensions
{
    public static BlobContainerClient GetBlobContainerClient(this IRepositoryOptions container)
    {
        return container.GetBlobServiceClient().GetBlobContainerClient(container.ContainerName);
    }

    public static BlobContainerClient GetBlobContainerClient(this IRepositoryOptions container, BlobClientOptions options)
    {
        return container.GetBlobServiceClient(options).GetBlobContainerClient(container.ContainerName);
    }
}