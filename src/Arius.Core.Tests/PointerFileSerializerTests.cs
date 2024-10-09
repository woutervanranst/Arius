using Arius.Core.Domain.Storage.FileSystem;
using Arius.Core.Infrastructure.Storage.LocalFileSystem;
using Arius.Core.Tests.Fixtures;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Arius.Core.Tests;

public class PointerFileSerializerTests : TestBase
{
    private readonly PointerFileSerializer pointerFileSerializer = new(NullLogger<PointerFileSerializer>.Instance);

    protected override AriusFixture GetFixture()
    {
        return new FixtureBuilder()
            .WithMockedStorageAccountFactory()
            .WithUniqueContainerName()
            .Build();
    }

    protected override void ConfigureOnceForFixture()
    {
    }

    
    [Fact]
    public void CreateIfNotExists_DoesNotExist_Created()
    {
        // Arrange
        var relativeName = "directory/File1.txt";
        var fpwh         = GivenSourceFolderHavingFilePair(relativeName, FilePairType.BinaryFileOnly, 100);
        fpwh.PointerFile.Exists.Should().BeFalse();

        // Act
        var r = pointerFileSerializer.CreateIfNotExists(fpwh.BinaryFile);

        // Assert
        r.Created.Should().Be(PointerFileSerializer.CreationResult.Created);
        r.PointerFileWithHash.Exists.Should().BeTrue();

        r.PointerFileWithHash.CreationTimeUtc.Should().Be(fpwh.PointerFile.CreationTimeUtc);
        r.PointerFileWithHash.LastWriteTimeUtc.Should().Be(fpwh.PointerFile.LastWriteTimeUtc);
    }

    [Fact]
    public void CreateIfNotExists_ExistsIdentical_NotCreated()
    {
        // Arrange
        var relativeName = "directory/File1.txt";
        var fpwh         = GivenSourceFolderHavingFilePair(relativeName, FilePairType.BinaryFileWithPointerFile, 100);
        fpwh.PointerFile.Exists.Should().BeTrue();

        // Act
        var r = pointerFileSerializer.CreateIfNotExists(fpwh.BinaryFile);

        // Assert
        r.Created.Should().Be(PointerFileSerializer.CreationResult.Existed);
        r.PointerFileWithHash.Exists.Should().BeTrue();
    }


    [Fact]
    public void CreateIfNotExists_ExistsOutdated_Created()
    {
        // Arrange
        var relativeName = "directory/File1.txt";
        var fpwh         = GivenSourceFolderHavingFilePair(relativeName, FilePairType.BinaryFileWithPointerFile, 100);
        fpwh.PointerFile.Exists.Should().BeTrue();
        fpwh.PointerFile.LastWriteTimeUtc = fpwh.PointerFile.LastWriteTimeUtc!.Value.AddDays(-1);

        // Act
        var r = pointerFileSerializer.CreateIfNotExists(fpwh.BinaryFile);

        // Assert
        r.Created.Should().Be(PointerFileSerializer.CreationResult.Overwritten);
        r.PointerFileWithHash.Exists.Should().BeTrue();
        r.PointerFileWithHash.LastWriteTimeUtc.Should().Be(fpwh.PointerFile.LastWriteTimeUtc);
    }

    [Fact]
    public void CreateIfNotExists_Broken_Created()
    {
        // Arrange
        var relativeName = "directory/File1.txt";
        var fpwh         = GivenSourceFolderHavingFilePair(relativeName, FilePairType.BinaryFileWithPointerFile, 100);
        System.IO.File.WriteAllText(fpwh.PointerFile.FullName, "i am a broken record");

        // Act
        var r = pointerFileSerializer.CreateIfNotExists(fpwh.BinaryFile);

        r.Created.Should().Be(PointerFileSerializer.CreationResult.Overwritten);
    }


    [Fact]
    public void FromExistingPointerFile_DoesNotExist_FileNotFoundException()
    {
        // Arrange
        var relativeName = "directory/File1.txt";
        var fpwh         = GivenSourceFolderHavingFilePair(relativeName, FilePairType.BinaryFileOnly, 100);
        fpwh.PointerFile.Exists.Should().BeFalse();

        // Act
        Action act = () => pointerFileSerializer.FromExistingPointerFile(fpwh.PointerFile);

        // Assert
        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void FromExistingPointerFile_Broken_InvalidDataException()
    {
        // Arrange
        var relativeName = "directory/File1.txt";
        var fpwh         = GivenSourceFolderHavingFilePair(relativeName, FilePairType.BinaryFileWithPointerFile, 100);
        System.IO.File.WriteAllText(fpwh.PointerFile.FullName, "i am a broken record");

        // Act
        Action act = () => pointerFileSerializer.FromExistingPointerFile(fpwh.PointerFile);

        // Assert
        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void FromExistingPointerFile_Exists_Valid()
    {
        // Arrange
        var relativeName = "directory/File1.txt";
        var fpwh         = GivenSourceFolderHavingFilePair(relativeName, FilePairType.BinaryFileWithPointerFile, 100);

        // Act
        var r = pointerFileSerializer.FromExistingPointerFile(fpwh.PointerFile);

        // Assert
        r.Hash.Should().Be(fpwh.Hash);
        r.CreationTimeUtc.Should().Be(fpwh.PointerFile.CreationTimeUtc);
        r.LastWriteTimeUtc.Should().Be(fpwh.PointerFile.LastWriteTimeUtc);
    }
}