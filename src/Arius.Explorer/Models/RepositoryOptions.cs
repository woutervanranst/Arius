using Arius.Explorer.Shared.Services;
using System.IO;
using System.Text.Json.Serialization;

namespace Arius.Explorer.Models;

public record RepositoryOptions
{
    public string LocalDirectoryPath { get; set; } = "";

    public string AccountName { get; set; } = "";
    public string AccountKeyProtected { get; set; } = "";
    public string ContainerName { get; set; } = "";
    public string PassphraseProtected { get; set; } = "";

    public DateTime LastOpened { get; set; }

    [JsonIgnore]
    public DirectoryInfo LocalDirectory => new(LocalDirectoryPath);

    [JsonIgnore]
    public string AccountKey => string.IsNullOrEmpty(AccountKeyProtected) ? "" : AccountKeyProtected.Unprotect();

    [JsonIgnore]
    public string Passphrase => string.IsNullOrEmpty(PassphraseProtected) ? "" : PassphraseProtected.Unprotect();
}