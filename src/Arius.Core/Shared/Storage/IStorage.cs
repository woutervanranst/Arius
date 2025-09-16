using Azure.Storage.Blobs.Models;
using FluentResults;

namespace Arius.Core.Shared.Storage;

/// <summary>
/// Storage metadata containing essential blob properties.
/// </summary>
internal record StorageProperties(
    string? ContentType,
    IDictionary<string, string>? Metadata,
    StorageTier? StorageTier
);

/// <summary>
/// Provides low-level blob storage operations for managing containers and binary data streams.
/// </summary>
internal interface IStorage
{
    Task<bool>               CreateContainerIfNotExistsAsync();
    Task<bool>               ContainerExistsAsync();
    IAsyncEnumerable<string> GetNamesAsync(string prefix, CancellationToken cancellationToken = default);
    Task<Result<Stream>>     OpenReadAsync(string blobName, IProgress<long>? progress = default, CancellationToken cancellationToken = default);
    Task<Result<Stream>>     OpenWriteAsync(string blobName, bool throwOnExists = false, IDictionary<string, string>? metadata = default, string? contentType = default, IProgress<long>? progress = default, CancellationToken cancellationToken = default);
    Task<StorageProperties?> GetPropertiesAsync(string blobName, CancellationToken cancellationToken = default);
    Task                     DeleteBlobAsync(string blobName, CancellationToken cancellationToken = default);
    Task                     SetAccessTierAsync(string blobName, AccessTier tier);
    Task                     StartHydrationAsync(string sourceBlobName, string targetBlobName, RehydratePriority priority);
}