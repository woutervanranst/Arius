using Arius.Core.Shared.Hashing;
using Arius.Core.Shared.Storage;
using Arius.Core.Tests.Helpers.Fixtures;
using FluentResults;
using NSubstitute;
using System.Formats.Tar;

namespace Arius.Core.Tests.Helpers.Builders;

internal class MockArchiveStorageBuilder
{
    private readonly Fixture                  fixture;

    // /chunks/ folder
    private readonly Dictionary<Hash, byte[]> chunks                      = new();
    private readonly HashSet<Hash>            chunksInArchiveTier         = new();
    private readonly HashSet<Hash>            chunksRehydrating           = new();

    // /chunks-rehydrated/ folder
    private readonly Dictionary<Hash, byte[]> chunksRehydrated            = new();
    private readonly HashSet<Hash>            chunksRehydratedRehydrating = new();
    private readonly HashSet<Hash>            chunksRehydratedArchiveTier = new();

    public MockArchiveStorageBuilder(Fixture fixture)
    {
        this.fixture = fixture;
    }

    public MockArchiveStorageBuilder AddChunks_BinaryChunk(Hash hash, byte[] content, StorageTier tier = StorageTier.Hot)
    {
        chunks[hash] = content;
        if (tier == StorageTier.Archive)
        {
            chunksInArchiveTier.Add(hash);
        }
        return this;
    }

    public MockArchiveStorageBuilder AddChunks_Rehydrating_BinaryChunk(Hash hash)
    {
        chunksRehydrating.Add(hash);
        return this;
    }

    public MockArchiveStorageBuilder AddTarChunk(out Hash tarHash, Action<TarChunkBuilder> configureTar, StorageTier tier = StorageTier.Hot)
    {
        var tarBuilder = new TarChunkBuilder(fixture);
        configureTar(tarBuilder);
        var tar = tarBuilder.Build();
        chunks[tar.Hash] = tar.Content;
        if (tier == StorageTier.Archive)
        {
            chunksInArchiveTier.Add(tar.Hash);
        }
        tarHash = tar.Hash;
        return this;
    }

    public MockArchiveStorageBuilder AddChunksRehydrated_BinaryChunk(Hash hash, byte[] content)
    {
        chunksRehydrated[hash] = content;
        return this;
    }

    public MockArchiveStorageBuilder AddChunksRehydrated_Rehydrating_BinaryChunk(Hash hash, byte[] content)
    {
        chunksRehydratedRehydrating.Add(hash);
        return this;
    }

    public MockArchiveStorageBuilder AddChunksRehydrated_InArchiveTier_BinaryChunk(Hash hash, byte[] content)
    {
        chunksRehydratedArchiveTier.Add(hash);
        return this;
    }

    public MockArchiveStorageBuilder AddHydratedTarChunk(out Hash tarHash, Action<TarChunkBuilder> configureTar)
    {
        var tarBuilder = new TarChunkBuilder(fixture);
        configureTar(tarBuilder);
        var tar = tarBuilder.Build();
        chunksRehydrated[tar.Hash] = tar.Content;
        tarHash = tar.Hash;
        return this;
    }

    public IArchiveStorage Build()
    {
        var storageMock = Substitute.For<IArchiveStorage>();
        
        storageMock.ContainerExistsAsync()
            .Returns(Task.FromResult(true));
            
        storageMock.OpenReadChunkAsync(Arg.Any<Hash>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var hash = callInfo.Arg<Hash>();
                
                // If this chunk is explicitly marked as archived, return archived error
                if (chunksInArchiveTier.Contains(hash))
                {
                    return Task.FromResult(Result.Fail<Stream>(new BlobArchivedError(hash.ToString())));
                }

                // If this chunk is currently rehydrating, return rehydrating error
                if (chunksRehydrating.Contains(hash))
                {
                    return Task.FromResult(Result.Fail<Stream>(new BlobRehydratingError(hash.ToString())));
                }
                
                // Otherwise, return regular chunk content
                if (chunks.TryGetValue(hash, out var content))
                {
                    return Task.FromResult(Result.Ok<Stream>(new MemoryStream(content)));
                }
                
                return Task.FromResult(Result.Fail<Stream>(new BlobNotFoundError(hash.ToString())));
            });
            
        storageMock.OpenReadHydratedChunkAsync(Arg.Any<Hash>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var hash = callInfo.Arg<Hash>();

                // If this chunk is currently rehydrating, return rehydrating error
                if (chunksRehydratedRehydrating.Contains(hash))
                {
                    return Task.FromResult(Result.Fail<Stream>(new BlobRehydratingError(hash.ToString())));
                }

                // If this chunk is still in archive tier, return archived error (this should not happen in practice)
                if (chunksRehydratedArchiveTier.Contains(hash))
                {
                    return Task.FromResult(Result.Fail<Stream>(new BlobArchivedError(hash.ToString())));
                }

                // Only hydrated chunks can be read via this method
                if (chunksRehydrated.TryGetValue(hash, out var content))
                {
                    return Task.FromResult(Result.Ok<Stream>(new MemoryStream(content)));
                }
                
                return Task.FromResult(Result.Fail<Stream>(new BlobNotFoundError(hash.ToString())));
            });
            
        return storageMock;
    }

    public class TarChunkBuilder
    {
        private readonly Fixture                           fixture;
        private readonly List<(Hash hash, byte[] content)> binaries = new();

        internal TarChunkBuilder(Fixture fixture)
        {
            this.fixture = fixture;
        }

        public TarChunkBuilder AddBinary(Hash hash, byte[] content)
        {
            binaries.Add((hash, content));
            return this;
        }

        internal (Hash Hash, byte[] Content) Build()
        {
            using var memoryStream = new MemoryStream();
            using var tarWriter = new TarWriter(memoryStream);

            foreach (var (hash, content) in binaries)
            {
                var entry = new PaxTarEntry(TarEntryType.RegularFile, hash.ToString())
                {
                    DataStream = new MemoryStream(content)
                };
                tarWriter.WriteEntry(entry);
            }

            var tarContent = memoryStream.ToArray();

            var hasher = new Sha256Hasher(Fixture.PASSPHRASE);
            var tarHash = hasher.GetHashAsync(tarContent).Result;

            return (tarHash, tarContent);
        }
    }
}