using Arius.Core.Domain.Storage.FileSystem;
using Arius.Core.Infrastructure.Storage.LocalFileSystem;
using Arius.Core.New.UnitTests.Fixtures;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Arius.Core.New.UnitTests;

public class LocalFileSystemTests_1 : TestBase
{
    protected override AriusFixture GetFixture()
    {
        return FixtureBuilder.Create()
            .WithUniqueContainerName()
            .Build();
    }

    protected override void ConfigureOnceForFixture()
    {
        GivenSourceFolderHavingFilePair("file1.txt",                      FilePairType.BinaryFileOnly, 0, FileAttributes.Normal); // Normal file
        GivenSourceFolderHavingFilePair("file2.txt",                      FilePairType.BinaryFileOnly, 0, FileAttributes.Normal); // Another normal file
        GivenSourceFolderHavingFilePair("subdir/file3.txt",               FilePairType.BinaryFileOnly, 0, FileAttributes.Normal); // Normal file in a subdirectory
        GivenSourceFolderHavingFilePair("subdir/file4.txt",               FilePairType.BinaryFileOnly, 0, FileAttributes.Normal); // Another normal file in a subdirectory
        GivenSourceFolderHavingFilePair("file5_hidden.txt",               FilePairType.BinaryFileOnly, 0, FileAttributes.Hidden); // Hidden file
        GivenSourceFolderHavingFilePair("subdir2/@eaDir/file6.txt",       FilePairType.BinaryFileOnly, 0, FileAttributes.Normal); // File in excluded directory
        GivenSourceFolderHavingFilePair("subdir2/eaDir/file7.txt",        FilePairType.BinaryFileOnly, 0, FileAttributes.Normal); // Another file in excluded directory
        GivenSourceFolderHavingFilePair("subdir2/SynoResource/file8.txt", FilePairType.BinaryFileOnly, 0, FileAttributes.Normal); // File in another excluded directory
        GivenSourceFolderHavingFilePair("subdir2/file9.txt",              FilePairType.BinaryFileOnly, 0, FileAttributes.Normal); // Normal file
        GivenSourceFolderHavingFilePair("subdir2/autorun.ini",            FilePairType.BinaryFileOnly, 0, FileAttributes.Normal); // Excluded file by name
        GivenSourceFolderHavingFilePair("subdir2/thumbs.db",              FilePairType.BinaryFileOnly, 0, FileAttributes.Normal); // Excluded file by name
        GivenSourceFolderHavingFilePair("subdir2/.ds_store",              FilePairType.BinaryFileOnly, 0, FileAttributes.Normal); // Excluded file by name
    }

    [Fact]
    public void EnumerateFiles_WithNormalAndHiddenNestedFiles_ShouldReturnOnlyExpectedOnesInOrder()
    {
        // Arrange
        var fs = new LocalFileSystem(NullLogger<LocalFileSystem>.Instance);

        var expectedRelativePaths = new[]
        {
            "file1.txt",
            "file2.txt",
            "subdir/file3.txt",
            "subdir/file4.txt",
            "subdir2/file9.txt"
        };

        // Act
        var files = fs.EnumerateFiles(Fixture.TestRunSourceFolder).ToList();

        // Assert
        Assert.NotNull(files);
        Assert.Equal(5, files.Count); // Only five normal files should be returned, excluding hidden and excluded files

        var actualRelativePaths = files
            .Select(f => f.GetRelativeNamePlatformNeutral(Fixture.TestRunSourceFolder));

        actualRelativePaths.Should().BeEquivalentTo(expectedRelativePaths);
    }
}