using Arius.Core.Infrastructure.Storage.LocalFileSystem;
using Arius.Core.New.UnitTests.Fixtures;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Arius.Core.New.UnitTests;

public class LocalFileSystemTests : TestBase
{
    protected override AriusFixture ConfigureFixture()
    {
        return FixtureBuilder.Create()
            .WithSourceFolderHavingRandomFile("file1.txt", 0, FileAttributes.Normal)                          // Normal file
            .WithSourceFolderHavingRandomFile("file2.txt", 0, FileAttributes.Normal)                          // Another normal file
            .WithSourceFolderHavingRandomFile("subdir/file3.txt", 0, FileAttributes.Normal)                   // Normal file in a subdirectory
            .WithSourceFolderHavingRandomFile("subdir/file4.txt", 0, FileAttributes.Normal)                   // Another normal file in a subdirectory
            .WithSourceFolderHavingRandomFile("file5_hidden.txt", 0, FileAttributes.Hidden)                   // Hidden file
            .WithSourceFolderHavingRandomFile("subdir2/@eaDir/file6.txt", 0, FileAttributes.Normal)           // File in excluded directory
            .WithSourceFolderHavingRandomFile("subdir2/eaDir/file7.txt", 0, FileAttributes.Normal)            // Another file in excluded directory
            .WithSourceFolderHavingRandomFile("subdir2/SynoResource/file8.txt", 0, FileAttributes.Normal)     // File in another excluded directory
            .WithSourceFolderHavingRandomFile("subdir2/file9.txt", 0, FileAttributes.Normal)                  // Normal file
            .WithSourceFolderHavingRandomFile("subdir2/autorun.ini", 0, FileAttributes.Normal)                // Excluded file by name
            .WithSourceFolderHavingRandomFile("subdir2/thumbs.db", 0, FileAttributes.Normal)                  // Excluded file by name
            .WithSourceFolderHavingRandomFile("subdir2/.ds_store", 0, FileAttributes.Normal)                  // Excluded file by name
            .Build();
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
        var files = fs.EnumerateFiles(Fixture.SourceFolder).ToList();

        // Assert
        Assert.NotNull(files);
        Assert.Equal(5, files.Count); // Only five normal files should be returned, excluding hidden and excluded files

        var actualRelativePaths = files
            .Select(f => f.GetRelativeNamePlatformNeutral(Fixture.SourceFolder));

        actualRelativePaths.SequenceEqual(expectedRelativePaths)
            .Should().BeTrue();
    }
}