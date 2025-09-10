using Arius.Core.Shared.Hashing;
using FluentResults;
using System.IO.Compression;
using Zio;

namespace Arius.Core.Shared.Storage;

/// <summary>
/// Provides archive-specific storage operations for managing chunks and application state.
/// Handles both chunked file data and state information for the archival system.
/// </summary>
internal interface IArchiveStorage
{
    // Container
    Task<bool>               CreateContainerIfNotExistsAsync();
    Task<bool>               ContainerExistsAsync();
    
    // States
    IAsyncEnumerable<string> GetStates(CancellationToken cancellationToken = default);
    Task                     DownloadStateAsync(string stateName, FileEntry targetFile, CancellationToken cancellationToken = default);
    Task                     UploadStateAsync(string stateName, FileEntry sourceFile, CancellationToken cancellationToken = default);
    
    // Chunks
    Task<Result<Stream>> OpenReadChunkAsync(Hash h, CancellationToken cancellationToken = default);
    Task<Result<Stream>> OpenReadHydratedChunkAsync(Hash h, CancellationToken cancellationToken = default);
    Task<Stream>         OpenWriteChunkAsync(Hash h, CompressionLevel compressionLevel, string contentType, IDictionary<string, string> metadata = default, IProgress<long> progress = default, CancellationToken cancellationToken = default);
    Task<StorageTier>    SetChunkStorageTierPerPolicy(Hash h, long length, StorageTier targetTier);
    Task                 StartHydrationAsync(Hash hash, RehydratePriority priority);
}