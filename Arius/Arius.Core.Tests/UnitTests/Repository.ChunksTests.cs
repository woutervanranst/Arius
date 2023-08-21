using Arius.Core.Repositories;
using Arius.Core.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Arius.Core.Tests.UnitTests;

class ChunkRepositoryTests : TestBase
{
    [Test]
    public async Task UploadChunkAsync_ChunkAlreadyExistsButNotInDb_GracefulRecovery()
    {
        // Given
        TestSetup.StageArchiveTestDirectory(out FileInfo file);
        await ArchiveCommand();

        var bfi = FileSystemService.GetBinaryFileInfo(file);
        var fs  = new FileService(NullLogger<FileService>.Instance, new SHA256Hasher(Repository.Options.Passphrase));
        var bf  = await fs.GetExistingBinaryFileAsync(TestSetup.ArchiveTestDirectory, bfi, false);

        var ce0  = await Repository.GetChunkEntryAsync(bf.BinaryHash);
        var bc0  = await Repository.CountBinariesAsync();
        var cce0 = await Repository.CountChunkEntriesAsync();

        // When the db is 'lost'
        //file.Delete();
        await Repository.DeleteChunkEntryAsync(bf.ChunkHash);

        // Then
        var bc1  = await Repository.CountBinariesAsync();
        bc1.Should().Be(bc0 - 1);

        var cce1 = await Repository.CountChunkEntriesAsync();
        cce1.Should().Be(cce0 - 1);

        Assert.CatchAsync(async () => await Repository.GetChunkEntryAsync(bf.ChunkHash));

        // When the archivecommand is re-run
        await ArchiveCommand();

        // Then the db is gracefully recovered
        var ce2 = await Repository.GetChunkEntryAsync(bf.BinaryHash);

        ce2.AccessTier.Should().Be(ce0.AccessTier);
        ce2.ArchivedLength.Should().Be(ce0.ArchivedLength);
        ce2.ChunkCount.Should().Be(ce0.ChunkCount);
        ce2.IncrementalLength.Should().Be(ce0.IncrementalLength);
        ce2.OriginalLength.Should().Be(ce0.OriginalLength);
    }

    [Test]
    public async Task SetAllChunks()
    {
        // the tier of a chunkenetry of a chunked binary in the db is not set
        // archive tier things are not updated


        // restore chunked binary


        // chunk reuses

        throw new NotImplementedException();

    }

    [Test]
    public async Task AnArchiveWithAllChunksInCoolAreMigratedToCold()
    {
        // the tier of a chunkenetry of a chunked binary in the db is not set
        // archive tier things are not updated

        throw new NotImplementedException();
    }
}