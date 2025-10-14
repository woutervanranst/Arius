using Arius.Core.Shared.Hashing;
using Arius.Core.Shared.Storage;
using FluentResults;
using System.Collections.Concurrent;
using System.IO.Compression;
using Zio;

namespace Arius.Core.Tests.Helpers.Fakes;

internal class FakeArchiveStorage : IArchiveStorage
{
    private readonly ConcurrentDictionary<Hash, FakeArchiveStorageChunk> chunks = new();
    private readonly ConcurrentDictionary<Hash, FakeArchiveStorageChunk> hydratedChunks = new();
    private readonly ConcurrentDictionary<string, byte[]>                states = new(StringComparer.OrdinalIgnoreCase);

    private int containerCreated;

    public IReadOnlyDictionary<Hash, FakeArchiveStorageChunk> StoredChunks => chunks;

    public IReadOnlyDictionary<string, byte[]> StoredStates => states;

    public List<string> UploadedStates { get; } = new();

    public Task<bool> CreateContainerIfNotExistsAsync()
    {
        var created = Interlocked.Exchange(ref containerCreated, 1) == 0;
        return Task.FromResult(created);
    }

    public Task<bool> ContainerExistsAsync() => Task.FromResult(containerCreated == 1);

    public IAsyncEnumerable<string> GetStates(CancellationToken cancellationToken = default)
    {
        var orderedStates = states.Keys
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return orderedStates.ToAsyncEnumerable();
    }

    public Task DownloadStateAsync(string stateName, FileEntry targetFile, CancellationToken cancellationToken = default)
    {
        if (!states.TryGetValue(stateName, out var content))
            throw new InvalidOperationException($"State '{stateName}' does not exist in fake storage.");

        targetFile.Directory.Create();

        using var stream = targetFile.Open(FileMode.Create, FileAccess.Write, FileShare.None);
        stream.Write(content);
        return Task.CompletedTask;
    }

    public async Task UploadStateAsync(string stateName, FileEntry sourceFile, CancellationToken cancellationToken = default)
    {
        await using var sourceStream = sourceFile.Open(FileMode.Open, FileAccess.Read, FileShare.Read);
        using var memoryStream = new MemoryStream();
        await sourceStream.CopyToAsync(memoryStream, cancellationToken);

        states[stateName] = memoryStream.ToArray();
        UploadedStates.Add(stateName);
    }

    public Task<Result<Stream>> OpenReadChunkAsync(Hash h, CancellationToken cancellationToken = default)
    {
        if (chunks.TryGetValue(h, out var chunk))
        {
            return Task.FromResult(Result.Ok<Stream>(new MemoryStream(chunk.Content, writable: false)));
        }

        if (hydratedChunks.TryGetValue(h, out var hydratedChunk))
        {
            return Task.FromResult(Result.Ok<Stream>(new MemoryStream(hydratedChunk.Content, writable: false)));
        }

        return Task.FromResult(Result.Fail<Stream>(new BlobNotFoundError(h.ToString())));
    }

    public Task<Result<Stream>> OpenReadHydratedChunkAsync(Hash h, CancellationToken cancellationToken = default)
    {
        if (hydratedChunks.TryGetValue(h, out var chunk))
        {
            return Task.FromResult(Result.Ok<Stream>(new MemoryStream(chunk.Content, writable: false)));
        }

        return Task.FromResult(Result.Fail<Stream>(new BlobNotFoundError(h.ToString())));
    }

    public virtual Task<Result<Stream>> OpenWriteChunkAsync(Hash h, CompressionLevel compressionLevel, string contentType, IDictionary<string, string>? metadata = null, IProgress<long>? progress = null, bool overwrite = false, CancellationToken cancellationToken = default)
    {
        if (!overwrite && chunks.ContainsKey(h))
        {
            return Task.FromResult(Result.Fail<Stream>(new BlobAlreadyExistsError(h.ToString())));
        }

        var chunk = new FakeArchiveStorageChunk
        {
            ContentType      = contentType,
            CompressionLevel = compressionLevel,
            Metadata         = metadata != null ? new Dictionary<string, string>(metadata, StringComparer.OrdinalIgnoreCase) : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            StorageTier      = StorageTier.Cool
        };

        var recordingStream = new RecordingMemoryStream(bytes =>
        {
            chunk.Content = bytes;
            chunk.ContentLength = bytes.LongLength;
            chunks[h] = chunk;
        });

        return Task.FromResult(Result.Ok<Stream>(recordingStream));
    }

    public Task<StorageProperties?> GetChunkPropertiesAsync(Hash h, CancellationToken cancellationToken = default)
    {
        if (chunks.TryGetValue(h, out var chunk))
        {
            return Task.FromResult<StorageProperties?>(new StorageProperties(
                h.ToString(),
                chunk.ContentType,
                chunk.Metadata.Count == 0 ? null : new Dictionary<string, string>(chunk.Metadata, StringComparer.OrdinalIgnoreCase),
                chunk.StorageTier,
                chunk.ContentLength));
        }

        return Task.FromResult<StorageProperties?>(null);
    }

    public Task DeleteChunkAsync(Hash h, CancellationToken cancellationToken = default)
    {
        chunks.TryRemove(h, out _);
        hydratedChunks.TryRemove(h, out _);
        return Task.CompletedTask;
    }

    public Task SetChunkMetadataAsync(Hash h, IDictionary<string, string> metadata, CancellationToken cancellationToken = default)
    {
        if (chunks.TryGetValue(h, out var chunk))
        {
            chunk.Metadata = new Dictionary<string, string>(metadata, StringComparer.OrdinalIgnoreCase);
        }

        return Task.CompletedTask;
    }

    public Task<StorageTier> SetChunkStorageTierPerPolicy(Hash h, long length, StorageTier targetTier)
    {
        if (chunks.TryGetValue(h, out var chunk))
        {
            chunk.StorageTier = targetTier;
        }

        return Task.FromResult(targetTier);
    }

    public Task StartHydrationAsync(Hash hash, RehydratePriority priority)
    {
        if (chunks.TryGetValue(hash, out var chunk))
        {
            hydratedChunks[hash] = chunk;
        }

        return Task.CompletedTask;
    }

    private sealed class RecordingMemoryStream : MemoryStream
    {
        private readonly Action<byte[]> onDispose;

        public RecordingMemoryStream(Action<byte[]> onDispose) => this.onDispose = onDispose;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                onDispose(ToArray());
            }

            base.Dispose(disposing);
        }
    }
}

internal sealed class FakeArchiveStorageChunk
{
    public byte[] Content { get; set; } = Array.Empty<byte>();
    public long ContentLength { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public StorageTier StorageTier { get; set; } = StorageTier.Cool;
    public CompressionLevel CompressionLevel { get; set; }
}
