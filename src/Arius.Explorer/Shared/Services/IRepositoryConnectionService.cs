using System.Threading;
using System.Threading.Tasks;

namespace Arius.Explorer.Shared.Services;

/// <summary>
/// Provides access to repository specific operations that require communication with the backend.
/// </summary>
public interface IRepositoryConnectionService
{
    /// <summary>
    /// Attempts to open the repository described in <paramref name="request"/>.
    /// </summary>
    /// <param name="request">The user supplied configuration.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
    Task<RepositoryConnectionResult> TryConnectAsync(RepositoryConnectionRequest request, CancellationToken cancellationToken = default);
}
