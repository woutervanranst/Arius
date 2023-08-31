using System.IO;

namespace Arius.UI.ViewModels;

internal abstract record RepositoryOptions
{
    public required DirectoryInfo LocalDirectory { get; init; }

    public required string AccountName   { get; init; }
    public required string AccountKey    { get; init; }
    public required string ContainerName { get; init; }
    public required string Passphrase    { get; init; }
}

internal record RepositoryChosenMessage : RepositoryOptions
{
    public required object Sender { get; init; }
}

internal record ChooseRepositoryMessage : RepositoryOptions
{
    public required object Sender { get; init; }
}