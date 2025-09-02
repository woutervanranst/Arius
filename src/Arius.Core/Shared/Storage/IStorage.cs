using Azure.Storage.Blobs.Models;

namespace Arius.Core.Shared.Storage;

/// <summary>
/// Provides low-level blob storage operations for managing containers and binary data streams.
/// </summary>
public interface IStorage
{
    Task<bool>                     CreateContainerIfNotExistsAsync();
    Task<bool>                     ContainerExistsAsync();
    IAsyncEnumerable<string>       GetNamesAsync(string prefix, CancellationToken cancellationToken = default);
    Task<Stream>                   OpenReadAsync(string blobName, IProgress<long>? progress = default, CancellationToken cancellationToken = default);
    Task<Stream>                   OpenWriteAsync(string blobName, bool throwOnExists = false, IDictionary<string, string>? metadata = default, string? contentType = default, IProgress<long>? progress = default, CancellationToken cancellationToken = default);
    Task                           SetAccessTierAsync(string blobName, AccessTier tier);
}