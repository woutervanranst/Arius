using Arius.Core.Models;
using System.IO.Compression;

namespace Arius.Core.Services;

/// <summary>
/// Provides archive-specific storage operations for managing chunks and application state.
/// Handles both chunked file data and state information for the archival system.
/// </summary>
public interface IArchiveStorage
{
    Task<bool>               CreateContainerIfNotExistsAsync();
    Task<bool>               ContainerExistsAsync();
    IAsyncEnumerable<string> GetStates(CancellationToken cancellationToken = default);
    Task                     DownloadStateAsync(string stateName, FileInfo targetFile, CancellationToken cancellationToken = default);
    Task                     UploadStateAsync(string stateName, FileInfo sourceFile, CancellationToken cancellationToken = default);
    Task<Stream>             OpenReadChunkAsync(Hash h, CancellationToken cancellationToken = default);
    Task<Stream>             OpenWriteChunkAsync(Hash h, CompressionLevel compressionLevel, string contentType, IDictionary<string, string> metadata = default, IProgress<long> progress = default, CancellationToken cancellationToken = default);
    Task<StorageTier>        SetChunkStorageTierPerPolicy(Hash h, long length, StorageTier targetTier);
}