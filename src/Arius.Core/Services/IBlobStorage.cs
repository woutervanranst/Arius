using Azure.Storage.Blobs.Models;

namespace Arius.Core.Services;

public interface IBlobStorage
{
    Task<bool>                     CreateContainerIfNotExistsAsync();
    Task<bool>                     ContainerExistsAsync();
    IAsyncEnumerable<string>       GetBlobsAsync(string prefix, CancellationToken cancellationToken = default);
    Task<Stream>                   OpenReadAsync(string blobName, IProgress<long>? progress = default, CancellationToken cancellationToken = default);
    Task<Stream>                   OpenWriteAsync(string blobName, bool throwOnExists = false, IDictionary<string, string>? metadata = default, string? contentType = default, IProgress<long>? progress = default, CancellationToken cancellationToken = default);
    Task                           SetAccessTierAsync(string blobName, AccessTier tier);
}