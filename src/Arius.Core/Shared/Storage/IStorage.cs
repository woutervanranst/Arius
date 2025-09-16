using Azure.Storage.Blobs.Models;
using FluentResults;

namespace Arius.Core.Shared.Storage;

/// <summary>
/// Storage metadata containing essential blob properties.
/// </summary>
internal record StorageProperties(
    string? ContentType,
    IDictionary<string, string>? Metadata,
    StorageTier? StorageTier,
    long ContentLength
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
    Task<Result<Stream>>     OpenWriteAsync(string blobName, bool throwOnExists = false, IDictionary<string, string>? metadata = null, string? contentType = null, IProgress<long>? progress = null, CancellationToken cancellationToken = default);
    Task<StorageProperties?> GetPropertiesAsync(string blobName, CancellationToken cancellationToken = default);
    Task                     DeleteBlobAsync(string blobName, CancellationToken cancellationToken = default);
    Task                     SetAccessTierAsync(string blobName, AccessTier tier);
    Task                     SetMetadataAsync(string blobName, IDictionary<string, string> metadata, CancellationToken cancellationToken = default);
    Task                     StartHydrationAsync(string sourceBlobName, string targetBlobName, RehydratePriority priority);
}