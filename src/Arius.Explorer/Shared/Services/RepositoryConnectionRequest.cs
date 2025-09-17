using System;

namespace Arius.Explorer.Shared.Services;

/// <summary>
/// Represents the user supplied configuration that is required to open a repository.
/// </summary>
public sealed record RepositoryConnectionRequest(
    string LocalDirectoryPath,
    string AccountName,
    string AccountKey,
    string? ContainerName,
    string Passphrase)
{
    /// <summary>
    /// Validates the current request and throws an <see cref="ArgumentException"/> when one of the
    /// required fields is missing. The method returns the current instance to allow fluent usage.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when one of the mandatory parameters is empty.</exception>
    public RepositoryConnectionRequest EnsureIsValid()
    {
        if (string.IsNullOrWhiteSpace(LocalDirectoryPath))
            throw new ArgumentException("A local repository directory must be provided.", nameof(LocalDirectoryPath));

        if (string.IsNullOrWhiteSpace(AccountName))
            throw new ArgumentException("The storage account name is required.", nameof(AccountName));

        if (string.IsNullOrWhiteSpace(AccountKey))
            throw new ArgumentException("The storage account key is required.", nameof(AccountKey));

        return this;
    }
}
