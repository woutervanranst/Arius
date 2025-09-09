using Arius.Core.Shared.Hashing;
using Arius.Core.Shared.Storage;
using Arius.Core.Tests.Helpers.Fixtures;
using NSubstitute;
using System.Formats.Tar;

namespace Arius.Core.Tests.Helpers.Builders;

internal class MockArchiveStorageBuilder
{
    private readonly Fixture                  fixture;
    private readonly Dictionary<Hash, byte[]> chunks = new();

    public MockArchiveStorageBuilder(Fixture fixture)
    {
        this.fixture = fixture;
    }

    public MockArchiveStorageBuilder AddBinaryChunk(Hash hash, byte[] content)
    {
        chunks[hash] = content;
        return this;
    }

    public MockArchiveStorageBuilder AddTarChunk(out Hash tarHash, Action<TarChunkBuilder> configureTar)
    {
        var tarBuilder = new TarChunkBuilder(fixture);
        configureTar(tarBuilder);
        var tar = tarBuilder.Build();
        chunks[tar.Hash] = tar.Content;
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
                var content = chunks[hash];
                return Task.FromResult<Stream>(new MemoryStream(content));
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