using Arius.Core.Models;
using System.IO.Compression;

namespace Arius.Core.Services;

public interface IBlobStorage
{
    Task<bool>               CreateContainerIfNotExistsAsync();
    Task<bool>               ContainerExistsAsync();
    IAsyncEnumerable<string> GetStates(CancellationToken cancellationToken = default);
    Task                     DownloadStateAsync(string stateName, FileInfo targetFile, string passphrase, CancellationToken cancellationToken = default);
    Task                     UploadStateAsync(string stateName, FileInfo sourceFile, string passphrase, CancellationToken cancellationToken = default);
    Task<Stream>             OpenReadChunkAsync(Hash h, string passphrase, CancellationToken cancellationToken = default);
    Task<Stream>             OpenWriteChunkAsync(Hash h, string passphrase, CompressionLevel compressionLevel, string contentType, IDictionary<string, string> metadata = default, IProgress<long> progress = default, CancellationToken cancellationToken = default);
    Task<StorageTier>        SetChunkStorageTierPerPolicy(Hash h, long length, StorageTier targetTier);
}