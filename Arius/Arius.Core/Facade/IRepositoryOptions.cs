using Arius.Core.Commands;
using FluentValidation;

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



internal interface IContainerOptions : IStorageAccountOptions
{
    string ContainerName { get; }
}

internal record ContainerOptions : IContainerOptions
{
    public ContainerOptions(IStorageAccountOptions storageAccountOptions, string containerName)
    {
        this.AccountName   = storageAccountOptions.AccountName;
        this.AccountKey    = storageAccountOptions.AccountKey;
        this.ContainerName = containerName;
    }

    public string AccountName   { get; }
    public string AccountKey    { get; }
    public string ContainerName { get; }
}



internal interface IRepositoryOptions : IContainerOptions, ICommandOptions // TODO remove ICommandOptions
{
    string Passphrase { get; }

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
    public RepositoryOptions(IContainerOptions containerOptions, string passphrase)
    {
        AccountName   = containerOptions.AccountName;
        AccountKey    = containerOptions.AccountKey;
        ContainerName = containerOptions.ContainerName;
        Passphrase    = passphrase;
    }

    public string AccountName   { get; }
    public string AccountKey    { get; }
    public string ContainerName { get; }
    public string Passphrase    { get; }
}