using System;
using Arius.Core.Commands;
using Azure.Storage.Blobs;
using Azure.Storage;

namespace Arius.Core.Facade;

internal record StorageAccountOptions : CommandOptions
{
    public StorageAccountOptions(string accountName, string accountKey)
    {
        this.AccountName = accountName;
        this.AccountKey  = accountKey;
    }

    public string AccountName { get; }
    public string AccountKey  { get; }

    public override void Validate()
    {
        if (string.IsNullOrWhiteSpace(AccountName))
            throw new ArgumentException($"{nameof(AccountName)} must be specified", nameof(AccountName));

        if (string.IsNullOrWhiteSpace(AccountKey))
            throw new ArgumentException($"{nameof(AccountKey)} must be specified", nameof(AccountKey));
    }
}

internal static class StorageAccountOptionsExtensions
{
    public static BlobServiceClient GetBlobServiceClient(this StorageAccountOptions storageAccount)
    {
        return new BlobServiceClient(new Uri($"https://{storageAccount.AccountName}.blob.core.windows.net/"),
            new StorageSharedKeyCredential(storageAccount.AccountName, storageAccount.AccountKey));
    }
    public static BlobServiceClient GetBlobServiceClient(this StorageAccountOptions storageAccount, BlobClientOptions options)
    {
        return new BlobServiceClient(new Uri($"https://{storageAccount.AccountName}.blob.core.windows.net/"),
            new StorageSharedKeyCredential(storageAccount.AccountName, storageAccount.AccountKey),
            options);
    }
}