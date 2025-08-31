using Mediator;

namespace Arius.Core.Commands;

public abstract record RepositoryCommand : ICommand
{
    public required string AccountName   { get; init; }
    public required string AccountKey    { get; init; }
    public required string ContainerName { get; init; }
    public required string Passphrase    { get; init; } // TODO investigate SecureString
}