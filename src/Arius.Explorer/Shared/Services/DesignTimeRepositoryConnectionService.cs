using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Arius.Explorer.Shared.Services;

/// <summary>
/// A lightweight repository connection service that provides deterministic data for development and testing scenarios.
/// </summary>
public sealed class DesignTimeRepositoryConnectionService : IRepositoryConnectionService
{
    private static readonly IReadOnlyList<string> DefaultContainers =
    [
        "container1",
        "container2",
        "backups",
        "archives"
    ];

    public Task<RepositoryConnectionResult> TryConnectAsync(RepositoryConnectionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            request.EnsureIsValid();
        }
        catch (ArgumentException ex)
        {
            return Task.FromResult(RepositoryConnectionResult.Failure(ex.Message));
        }

        // In the design-time implementation we simply return a deterministic list of containers.
        return Task.FromResult(RepositoryConnectionResult.Success(DefaultContainers));
    }
}
