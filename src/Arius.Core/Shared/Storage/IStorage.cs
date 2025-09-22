using FluentResults;

namespace Arius.Core.Shared.Storage;

/// <summary>
/// Storage metadata containing essential blob properties.
/// </summary>
internal record StorageProperties(
    string Name,
    string? ContentType,
    IDictionary<string, string>? Metadata, // TODO when null?
    StorageTier? StorageTier, // TODO when null?
    long ContentLength
);

/// <summary>
/// Provides low-level blob storage operations for managing containers and binary data streams.
/// </summary>
internal interface IStorage
{
    Task<bool>                          CreateContainerIfNotExistsAsync();
    Task<bool>                          ContainerExistsAsync();
    IAsyncEnumerable<StorageProperties> GetAllAsync(string prefix, CancellationToken cancellationToken = default);
    Task<Result<Stream>>                OpenReadAsync(string blobName, IProgress<long>? progress = default, CancellationToken cancellationToken = default);
    Task<Result<Stream>>                OpenWriteAsync(string blobName, bool throwOnExists = false, IDictionary<string, string>? metadata = null, string? contentType = null, IProgress<long>? progress = null, CancellationToken cancellationToken = default);
    Task<StorageProperties?>            GetPropertiesAsync(string blobName, CancellationToken cancellationToken = default);
    Task                                DeleteAsync(string blobName, CancellationToken cancellationToken = default);
    Task                                SetAccessTierAsync(string blobName, StorageTier tier);
    Task                                SetMetadataAsync(string blobName, IDictionary<string, string> metadata, CancellationToken cancellationToken = default);
    Task                                StartHydrationAsync(string sourceBlobName, string targetBlobName, RehydratePriority priority);
}