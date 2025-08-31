using Arius.Core.Models;

namespace Arius.Core.Services;

public interface IBlobStorage
{
    Task<bool>               CreateContainerIfNotExistsAsync();
    Task<bool>               ContainerExistsAsync();
    IAsyncEnumerable<string> GetStates(CancellationToken cancellationToken = default);
    //Task<Stream>             OpenReadStateAsync(string stateName, CancellationToken cancellationToken = default);
    Task                     DownloadStateAsync(string stateName, FileInfo targetFile, CancellationToken cancellationToken = default);
    Task                     UploadStateAsync(string stateName, FileInfo sourceFile, CancellationToken cancellationToken = default);
    Task<Stream>             OpenReadChunkAsync(Hash h, string passphrase, CancellationToken cancellationToken = default);
    Task<Stream>             OpenWriteChunkAsync(Hash h, string passphrase, string contentType, IDictionary<string, string> metadata = default, IProgress<long> progress = default, CancellationToken cancellationToken = default);
    Task<StorageTier>        SetChunkStorageTierPerPolicy(Hash h, long length, StorageTier targetTier);
}