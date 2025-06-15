using Arius.Core.Models;

namespace Arius.Core.Repositories;

public interface IBlobStorage
{
    Task<bool> CreateContainerIfNotExistsAsync(CancellationToken cancellationToken = default);
    IAsyncEnumerable<string> GetStates();
    Task DownloadStateAsync(string stateName, FileInfo destination);
    Task UploadStateAsync(string stateName, FileInfo sourceFile, CancellationToken cancellationToken = default);
    Task<Stream> OpenWriteChunkAsync(Hash h, string contentType, IDictionary<string, string> metadata = default, IProgress<long> progress = default, CancellationToken cancellationToken = default);
    Task<StorageTier> SetChunkStorageTierPerPolicy(Hash h, long length, StorageTier targetTier);
}