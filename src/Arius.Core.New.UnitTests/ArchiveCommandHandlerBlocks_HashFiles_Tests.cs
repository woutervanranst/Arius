using Arius.Core.Domain;
using Arius.Core.Domain.Services;
using Arius.Core.Domain.Storage.FileSystem;
using Arius.Core.New.Commands.Archive;
using Arius.Core.New.UnitTests.Fixtures;
using FluentAssertions;
using NSubstitute;

namespace Arius.Core.New.UnitTests;

public class ArchiveCommandHandlerBlocks_HashFiles_Tests : TestBase
{
    protected override AriusFixture GetFixture()
    {
        return FixtureBuilder.Create().WithUniqueContainerName().Build();
    }

    protected override void ConfigureOnceForFixture()
    {
        //GivenSourceFolderHavingRandomFileWithPointerFile("file1.bin", 100);
        //GivenSourceFolderHavingRandomFile("file2.bin", 100);
        //GivenSourceFolderHavingRandomFileWithPointerFile("file3.bin.pointer.arius", someHash);
    }

    [Fact]
    public async Task HashFilesAsync_WhenFastHashWithPointerFileAndBinaryFileWithSameLastWriteTime_Fasthashed()
    {
        // Arrange
        var p1 = GivenSourceFolderHavingFilePair("file1.bin", FilePairType.BinaryFileWithPointerFile, 100);

        // ensure the last write time is the SAME
        p1.PointerFile!.LastWriteTimeUtc = p1.BinaryFile!.LastWriteTimeUtc;

        var hvp = Substitute.For<IHashValueProvider>();
        
        // Act
        var p2 = await ArchiveCommandHandler.HashFilesAsync(true, hvp, p1);

        // Assert
        hvp.DidNotReceive().GetHashAsync(Arg.Any<IBinaryFile>());

        p2.Should().NotBeNull();
        p2.BinaryFile.Hash.Should().Be(p2.PointerFile.Hash);
    }

    [Fact]
    public async Task HashFilesAsync_WhenFastHashWithPointerFileAndBinaryFileWithDifferentLastWriteTime_HashValueProviderCalled()
    {
        // Arrange
        var p1 = GivenSourceFolderHavingFilePair("file1.bin", FilePairType.BinaryFileWithPointerFile, 100);

        // ensure the last write time is DIFFERENT
        p1.PointerFile!.LastWriteTimeUtc = p1.BinaryFile!.LastWriteTimeUtc.Value.AddSeconds(-1);

        var hvp = Substitute.For<IHashValueProvider>();
        hvp.GetHashAsync(Arg.Any<IBinaryFile>()).Returns(p1.BinaryFile.Hash);

        // Act
        var p2 = await ArchiveCommandHandler.HashFilesAsync(true, hvp, p1);

        // Assert
        hvp.Received(1).GetHashAsync(Arg.Any<IBinaryFile>());

        p2.Should().NotBeNull();
        p2.BinaryFile.Hash.Should().Be(p2.PointerFile.Hash);
    }

    [Fact]
    public async Task HashFilesAsync_WhenWhenMismatchedPointerFile_Exception()
    {
        // Arrange
        var p1 = GivenSourceFolderHavingFilePair("file1.bin", FilePairType.BinaryFileWithPointerFile, 100);

        var hvp      = Substitute.For<IHashValueProvider>();
        var someHash = new Hash("abc".StringToBytes());
        hvp.GetHashAsync(Arg.Any<IBinaryFile>()).Returns(someHash);

        // Act
        Func<Task> act = async () => await ArchiveCommandHandler.HashFilesAsync(false, hvp, p1);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task HashFilesAsync_WhenPointerFileAndBinaryFile_HashValueProviderCalled()
    {
        // Arrange
        var p1 = GivenSourceFolderHavingFilePair("file1.bin", FilePairType.BinaryFileWithPointerFile, 100);

        var hvp = Substitute.For<IHashValueProvider>();
        hvp.GetHashAsync(Arg.Any<IBinaryFile>()).Returns(p1.BinaryFile.Hash);

        // Act
        var p2 = await ArchiveCommandHandler.HashFilesAsync(false, hvp, p1);

        // Assert
        hvp.Received(1).GetHashAsync(Arg.Any<IBinaryFile>());

        p2.Should().NotBeNull();
        p2.BinaryFile.Hash.Should().Be(p2.PointerFile.Hash);
    }

    [Fact]
    public async Task HandleFilesAsync_WhenPointerFileWithoutBinaryFile_OnlyPointerFile()
    {
        // Arrange
        var p0       = GivenSourceFolderHavingFilePair("file1.pointer.arius", FilePairType.PointerFileOnly, 0);
        var hvp      = Substitute.For<IHashValueProvider>();

        // Act
        var p2 = await ArchiveCommandHandler.HashFilesAsync(false, hvp, p0);

        // Assert
        p2.Should().NotBeNull();
        p2.PointerFile.Should().NotBeNull();
        p2.PointerFile.Hash.Should().Be(p0.Hash);
        p2.BinaryFile.Should().BeNull();
        hvp.DidNotReceive().GetHashAsync(Arg.Any<IBinaryFile>());
    }

    [Fact]
    public async Task HandleFilesAsync_WhenBinaryFileWithoutPointerFile_OnlyBinaryFile()
    {
        // Arrange
        var someHash = new Hash("abc".StringToBytes());
        var p0       = GivenSourceFolderHavingFilePair("file1.bin", FilePairType.BinaryFileOnly, 100);
        var hvp      = Substitute.For<IHashValueProvider>();
        hvp.GetHashAsync(Arg.Any<IBinaryFile>()).Returns(someHash);

        // Act
        var p2 = await ArchiveCommandHandler.HashFilesAsync(false, hvp, p0);

        // Assert
        p2.Should().NotBeNull();
        p2.BinaryFile.Should().NotBeNull();
        p2.BinaryFile.Hash.Should().Be(someHash);
        p2.PointerFile.Should().BeNull();
        hvp.Received(1).GetHashAsync(Arg.Any<IBinaryFile>());
    }
}