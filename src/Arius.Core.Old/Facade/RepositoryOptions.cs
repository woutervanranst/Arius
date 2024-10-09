using Azure.Storage.Blobs;

namespace Arius.Core.Facade;

internal record RepositoryOptions : StorageAccountOptions
{
    public RepositoryOptions(RepositoryOptions repositoryOptions) : base(repositoryOptions)
    {
        ContainerName = repositoryOptions.ContainerName;
        Passphrase    = repositoryOptions.Passphrase;
    }

    public RepositoryOptions(StorageAccountOptions storageAccountOptions, string containerName, string passphrase) : base(storageAccountOptions)
    {
        ContainerName = containerName;
        Passphrase    = passphrase;
    }

    public RepositoryOptions(string accountName, string accountKey, string containerName, string passphrase) : base(accountName, accountKey)
    {
        ContainerName = containerName;
        Passphrase    = passphrase;
    }

    public string ContainerName { get; }
    public string Passphrase    { get; }

    public override void Validate()
    {
        base.Validate();

        if (string.IsNullOrWhiteSpace(ContainerName))
            throw new ArgumentException($"{nameof(ContainerName)} must be specified", nameof(ContainerName));
        
        if (string.IsNullOrWhiteSpace(Passphrase))
            throw new ArgumentException($"{nameof(Passphrase)} must be specified", nameof(Passphrase));

        //    protected class Validator : AbstractValidator<IRepositoryOptions>
        //    {
        //        public Validator()
        //        {
        //            RuleFor(o => o.AccountName).NotEmpty();
        //            RuleFor(o => o.AccountKey).NotEmpty();
        //            RuleFor(o => o.ContainerName).NotEmpty();
        //            RuleFor(o => o.Passphrase).NotEmpty();
        //        }
        //    }
    }
}

internal static class RepositoryOptionsExtensions
{
    public static BlobContainerClient GetBlobContainerClient(this RepositoryOptions container)
    {
        return container.GetBlobServiceClient().GetBlobContainerClient(container.ContainerName);
    }

    public static BlobContainerClient GetBlobContainerClient(this RepositoryOptions container, BlobClientOptions options)
    {
        return container.GetBlobServiceClient(options).GetBlobContainerClient(container.ContainerName);
    }
}