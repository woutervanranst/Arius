namespace Arius.Core.Features;

public abstract record RepositoryCommandProperties : StorageAccountCommandProperties
{
    public required string ContainerName { get; init; }
    public required string Passphrase    { get; init; } // TODO investigate SecureString
}