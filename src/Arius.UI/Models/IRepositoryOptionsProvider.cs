using System.IO;

namespace Arius.UI.Models;

internal interface IRepositoryOptionsProvider
{
    DirectoryInfo LocalDirectory { get; }

    string AccountName   { get; }
    string AccountKey    { get; }
    string ContainerName { get; }
    string Passphrase    { get; }
}