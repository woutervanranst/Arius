using System;
using System.Collections.Generic;
using System.Linq;

namespace Arius.Explorer.Shared.Services;

/// <summary>
/// Describes the outcome of a repository connection attempt.
/// </summary>
public sealed class RepositoryConnectionResult
{
    private RepositoryConnectionResult(bool isSuccess, string? errorMessage, IReadOnlyList<string> containerNames)
    {
        IsSuccess = isSuccess;
        ErrorMessage = errorMessage;
        ContainerNames = containerNames;
    }

    /// <summary>
    /// Gets a value indicating whether the connection attempt succeeded.
    /// </summary>
    public bool IsSuccess { get; }

    /// <summary>
    /// Gets an optional error message describing why the connection failed.
    /// </summary>
    public string? ErrorMessage { get; }

    /// <summary>
    /// Gets the list of containers that were discovered for the repository.
    /// </summary>
    public IReadOnlyList<string> ContainerNames { get; }

    /// <summary>
    /// Creates a successful connection result.
    /// </summary>
    public static RepositoryConnectionResult Success(IEnumerable<string> containerNames)
    {
        if (containerNames is null)
            throw new ArgumentNullException(nameof(containerNames));

        var containers = containerNames
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new RepositoryConnectionResult(true, null, containers);
    }

    /// <summary>
    /// Creates a failed connection result with the provided error message.
    /// </summary>
    public static RepositoryConnectionResult Failure(string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(errorMessage))
            throw new ArgumentException("An error message must be provided when the connection fails.", nameof(errorMessage));

        return new RepositoryConnectionResult(false, errorMessage, Array.Empty<string>());
    }
}
