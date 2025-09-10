using Mediator;

namespace Arius.Core.Features;

public abstract record RepositoryCommand<TResponse> : ICommand<TResponse>
{
    public required string AccountName    { get; init; }
    public required string AccountKey     { get; init; }
    public required string ContainerName  { get; init; }
    public required string Passphrase     { get; init; } // TODO investigate SecureString
 
    internal        bool   UseRetryPolicy { get; init; } = true;
}