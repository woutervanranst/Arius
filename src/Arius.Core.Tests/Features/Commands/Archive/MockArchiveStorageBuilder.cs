using System.Collections.Concurrent;
using System.IO.Compression;
using Arius.Core.Shared.Hashing;
using Arius.Core.Shared.Storage;
using Arius.Core.Tests.Helpers.Fixtures;
using FluentResults;
using NSubstitute;
using Zio;

namespace Arius.Core.Tests.Features.Commands.Archive;

internal class MockArchiveStorageBuilder
{
    private readonly Fixture fixture;

    // Internal state for mock configuration
    private readonly Dictionary<Hash, FakeArchiveStorageChunk> chunks          = new();
    private readonly Dictionary<Hash, FakeArchiveStorageChunk> hydratedChunks  = new();
    private readonly Dictionary<string, byte[]>                states          = new(StringComparer.OrdinalIgnoreCase);
    private          bool                                      containerExists = true;

    // Track operations for potential assertions
    private readonly ConcurrentDictionary<Hash, FakeArchiveStorageChunk> writtenChunks  = new();
    private readonly List<string>                                        uploadedStates = new();

    // Error simulation
    private int               throwOnWriteFailureCount;
    private Func<Hash, bool>? throwOnWritePredicate;

    // Expose internal state for test assertions
    public IReadOnlyDictionary<Hash, FakeArchiveStorageChunk> StoredChunks   => chunks;
    public IReadOnlyDictionary<string, byte[]>                StoredStates   => states;
    public List<string>                                       UploadedStates => uploadedStates;

    public MockArchiveStorageBuilder(Fixture fixture)
    {
        this.fixture = fixture;
    }

    public MockArchiveStorageBuilder WithThrowOnWrite(int failureCount, Func<Hash, bool>? predicate = null)
    {
        throwOnWriteFailureCount = failureCount;
        throwOnWritePredicate    = predicate;
        return this;
    }

    public MockArchiveStorageBuilder WithContainerExists(bool exists = true)
    {
        containerExists = exists;
        return this;
    }

    public MockArchiveStorageBuilder AddChunk(Hash hash, byte[] content, string contentType = "application/octet-stream", StorageTier tier = StorageTier.Cool, CompressionLevel compressionLevel = CompressionLevel.Optimal, IDictionary<string, string>? metadata = null)
    {
        chunks[hash] = new FakeArchiveStorageChunk
        {
            Content          = content,
            ContentLength    = content.Length,
            ContentType      = contentType,
            StorageTier      = tier,
            CompressionLevel = compressionLevel,
            Metadata         = metadata != null ? new Dictionary<string, string>(metadata, StringComparer.OrdinalIgnoreCase) : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        };
        return this;
    }

    public MockArchiveStorageBuilder AddHydratedChunk(Hash hash, byte[] content, string contentType = "application/octet-stream", StorageTier tier = StorageTier.Cool, CompressionLevel compressionLevel = CompressionLevel.Optimal, IDictionary<string, string>? metadata = null)
    {
        hydratedChunks[hash] = new FakeArchiveStorageChunk
        {
            Content          = content,
            ContentLength    = content.Length,
            ContentType      = contentType,
            StorageTier      = tier,
            CompressionLevel = compressionLevel,
            Metadata         = metadata != null ? new Dictionary<string, string>(metadata, StringComparer.OrdinalIgnoreCase) : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        };
        return this;
    }

    public MockArchiveStorageBuilder AddState(string name, byte[] content)
    {
        states[name] = content;
        return this;
    }

    public IArchiveStorage Build()
    {
        var mock = Substitute.For<IArchiveStorage>();

        // Container operations
        var containerCreated = containerExists;
        mock.CreateContainerIfNotExistsAsync()
            .Returns(callInfo =>
            {
                var wasCreated = !containerCreated;
                containerCreated = true;
                return Task.FromResult(wasCreated);
            });

        mock.ContainerExistsAsync()
            .Returns(_ => Task.FromResult(containerCreated));

        // State operations
        mock.GetStates(Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var orderedStates = states.Keys
                    .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                return orderedStates.ToAsyncEnumerable();
            });

        mock.DownloadStateAsync(Arg.Any<string>(), Arg.Any<FileEntry>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var stateName  = callInfo.Arg<string>();
                var targetFile = callInfo.ArgAt<FileEntry>(1);

                if (!states.TryGetValue(stateName, out var content))
                    throw new InvalidOperationException($"State '{stateName}' does not exist in fake storage.");

                targetFile.Directory.Create();
                using var stream = targetFile.Open(FileMode.Create, FileAccess.Write, FileShare.None);
                stream.Write(content);
                return Task.CompletedTask;
            });

        mock.UploadStateAsync(Arg.Any<string>(), Arg.Any<FileEntry>(), Arg.Any<CancellationToken>())
            .Returns(async callInfo =>
            {
                var stateName         = callInfo.Arg<string>();
                var sourceFile        = callInfo.ArgAt<FileEntry>(1);
                var cancellationToken = callInfo.ArgAt<CancellationToken>(2);

                await using var sourceStream = sourceFile.Open(FileMode.Open, FileAccess.Read, FileShare.Read);
                using var       memoryStream = new MemoryStream();
                await sourceStream.CopyToAsync(memoryStream, cancellationToken);

                states[stateName] = memoryStream.ToArray();
                uploadedStates.Add(stateName);
            });

        // Chunk read operations
        mock.OpenReadChunkAsync(Arg.Any<Hash>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var hash = callInfo.Arg<Hash>();

                if (chunks.TryGetValue(hash, out var chunk))
                {
                    return Task.FromResult(Result.Ok<Stream>(new MemoryStream(chunk.Content, writable: false)));
                }

                if (hydratedChunks.TryGetValue(hash, out var hydratedChunk))
                {
                    return Task.FromResult(Result.Ok<Stream>(new MemoryStream(hydratedChunk.Content, writable: false)));
                }

                return Task.FromResult(Result.Fail<Stream>(new BlobNotFoundError(hash.ToString())));
            });

        mock.OpenReadHydratedChunkAsync(Arg.Any<Hash>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var hash = callInfo.Arg<Hash>();

                if (hydratedChunks.TryGetValue(hash, out var chunk))
                {
                    return Task.FromResult(Result.Ok<Stream>(new MemoryStream(chunk.Content, writable: false)));
                }

                return Task.FromResult(Result.Fail<Stream>(new BlobNotFoundError(hash.ToString())));
            });

        // Chunk write operation
        var remainingFailures = throwOnWriteFailureCount;
        mock.OpenWriteChunkAsync(Arg.Any<Hash>(), Arg.Any<CompressionLevel>(), Arg.Any<string>(), Arg.Any<IDictionary<string, string>>(), Arg.Any<IProgress<long>>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var hash             = callInfo.Arg<Hash>();
                var compressionLevel = callInfo.ArgAt<CompressionLevel>(1);
                var contentType      = callInfo.ArgAt<string>(2);
                var metadata         = callInfo.ArgAt<IDictionary<string, string>>(3);
                var overwrite        = callInfo.ArgAt<bool>(5);

                // Simulate write failure if configured
                if ((throwOnWritePredicate is null || throwOnWritePredicate(hash)) && Interlocked.Decrement(ref remainingFailures) >= 0)
                {
                    return Task.FromResult(Result.Fail<Stream>(new ExceptionalError(new IOException("Simulated upload failure"))));
                }

                if (!overwrite && (chunks.ContainsKey(hash) || writtenChunks.ContainsKey(hash)))
                {
                    return Task.FromResult(Result.Fail<Stream>(new BlobAlreadyExistsError(hash.ToString())));
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
                    chunk.Content       = bytes;
                    chunk.ContentLength = bytes.LongLength;
                    writtenChunks[hash] = chunk;
                    chunks[hash]        = chunk;
                });

                return Task.FromResult(Result.Ok<Stream>(recordingStream));
            });

        // Get chunk properties
        mock.GetChunkPropertiesAsync(Arg.Any<Hash>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var hash = callInfo.Arg<Hash>();

                if (chunks.TryGetValue(hash, out var chunk))
                {
                    return Task.FromResult<StorageProperties?>(new StorageProperties(
                        hash.ToString(),
                        chunk.ContentType,
                        chunk.Metadata.Count == 0 ? null : new Dictionary<string, string>(chunk.Metadata, StringComparer.OrdinalIgnoreCase),
                        chunk.StorageTier,
                        chunk.ContentLength));
                }

                return Task.FromResult<StorageProperties?>(null);
            });

        // Delete chunk
        mock.DeleteChunkAsync(Arg.Any<Hash>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var hash = callInfo.Arg<Hash>();
                chunks.Remove(hash);
                hydratedChunks.Remove(hash);
                writtenChunks.TryRemove(hash, out _);
                return Task.CompletedTask;
            });

        // Set chunk metadata
        mock.SetChunkMetadataAsync(Arg.Any<Hash>(), Arg.Any<IDictionary<string, string>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var hash     = callInfo.Arg<Hash>();
                var metadata = callInfo.ArgAt<IDictionary<string, string>>(1);

                if (chunks.TryGetValue(hash, out var chunk))
                {
                    chunk.Metadata = new Dictionary<string, string>(metadata, StringComparer.OrdinalIgnoreCase);
                }

                return Task.CompletedTask;
            });

        // Set storage tier
        mock.SetChunkStorageTierPerPolicy(Arg.Any<Hash>(), Arg.Any<long>(), Arg.Any<StorageTier>())
            .Returns(callInfo =>
            {
                var hash       = callInfo.Arg<Hash>();
                var targetTier = callInfo.ArgAt<StorageTier>(2);

                if (chunks.TryGetValue(hash, out var chunk))
                {
                    chunk.StorageTier = targetTier;
                }

                return Task.FromResult(targetTier);
            });

        // Start hydration
        mock.StartHydrationAsync(Arg.Any<Hash>(), Arg.Any<RehydratePriority>())
            .Returns(callInfo =>
            {
                var hash = callInfo.Arg<Hash>();

                if (chunks.TryGetValue(hash, out var chunk))
                {
                    hydratedChunks[hash] = chunk;
                }

                return Task.CompletedTask;
            });

        return mock;
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

    internal sealed class FakeArchiveStorageChunk
    {
        public byte[]                     Content          { get; set; } = [];
        public long                       ContentLength    { get; set; }
        public string                     ContentType      { get; set; } = string.Empty;
        public Dictionary<string, string> Metadata         { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public StorageTier                StorageTier      { get; set; } = StorageTier.Cool;
        public CompressionLevel           CompressionLevel { get; set; }
    }
}