using System.IO;

namespace Arius.UI.ViewModels;

internal interface IRepositoryOptions
{
    DirectoryInfo LocalDirectory { get; }

    string AccountName   { get; }
    string AccountKey    { get; }
    string ContainerName { get; }
    string Passphrase    { get; }
}

internal record RepositoryChosenMessage : IRepositoryOptions
{
    public required object Sender { get; init; }

    public required DirectoryInfo LocalDirectory { get; init; }

    public required string AccountName   { get; init; }
    public required string AccountKey    { get; init; }
    public required string ContainerName { get; init; }
    public required string Passphrase    { get; init; }
}

internal record ChooseRepositoryMessage : IRepositoryOptions
{
    public required object Sender { get; init; }

    public DirectoryInfo? LocalDirectory { get; init; }

    public string? AccountName   { get; init; }
    public string? AccountKey    { get; init; }
    public string? ContainerName { get; init; }
    public string? Passphrase    { get; init; }

}