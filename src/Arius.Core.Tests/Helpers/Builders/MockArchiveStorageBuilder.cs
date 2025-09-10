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
    private readonly Dictionary<Hash, byte[]> chunks = new();
    private readonly Dictionary<Hash, byte[]> hydratedChunks = new();
    private readonly HashSet<Hash>            archivedChunks = new();

    public MockArchiveStorageBuilder(Fixture fixture)
    {
        this.fixture = fixture;
    }

    public MockArchiveStorageBuilder AddBinaryChunk(Hash hash, byte[] content, StorageTier tier = StorageTier.Hot)
    {
        chunks[hash] = content;
        if (tier == StorageTier.Archive)
        {
            archivedChunks.Add(hash);
        }
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
            archivedChunks.Add(tar.Hash);
        }
        tarHash = tar.Hash;
        return this;
    }

    public MockArchiveStorageBuilder AddHydratedBinaryChunk(Hash hash, byte[] content)
    {
        hydratedChunks[hash] = content;
        return this;
    }

    public MockArchiveStorageBuilder AddHydratedTarChunk(out Hash tarHash, Action<TarChunkBuilder> configureTar)
    {
        var tarBuilder = new TarChunkBuilder(fixture);
        configureTar(tarBuilder);
        var tar = tarBuilder.Build();
        hydratedChunks[tar.Hash] = tar.Content;
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
                if (archivedChunks.Contains(hash))
                {
                    return Task.FromResult(Result.Fail<Stream>(new BlobArchivedError(hash.ToString())));
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
                
                // Only hydrated chunks can be read via this method
                if (hydratedChunks.TryGetValue(hash, out var content))
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