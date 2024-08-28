using Arius.Core.Infrastructure.Storage.LocalFileSystem;
using Arius.Core.New.Commands.Archive;
using Arius.Core.New.UnitTests.Fixtures;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Arius.Core.New.UnitTests;

public class ArchiveCommandHandlerBlocks_IndexFiles_Tests : TestBase
{
    protected override AriusFixture ConfigureFixture()
    {
        return FixtureBuilder.Create()
            .WithSourceFolderHavingRandomFile("file1.bin", 0, FileAttributes.Normal)                            // Binary file without a pointer

            .WithSourceFolderHavingRandomFile("file2.bin.pointer.arius", 0, FileAttributes.Normal)                    // Pointer file without a binary file

            .WithSourceFolderHavingRandomFile("file3.bin", 0, FileAttributes.Normal)                            // Binary file with a matching pointer file
            .WithSourceFolderHavingRandomFile("file3.bin.pointer.arius", 0, FileAttributes.Normal)             // Pointer file with a matching binary file

            .WithSourceFolderHavingRandomFile("folder1/file4.bin", 0, FileAttributes.Normal)                    // Binary file in a folder

            .WithSourceFolderHavingRandomFile("folder2/file4.bin.pointer.arius", 0, FileAttributes.Normal)             // Pointer file with the same name in another folder

            .WithSourceFolderHavingRandomFile("folder3/file5.bin", 0, FileAttributes.Normal)
            .WithSourceFolderHavingRandomFile("folder3/file5.bin.pointer.arius", 0, FileAttributes.Normal)
            .Build();
    }

    [Fact]
    public void IndexFiles_WithStagedFiles_ShouldMatchAccordingly()
    {
        // Arrange
        var fs = new LocalFileSystem(NullLogger<LocalFileSystem>.Instance);

        var expectedResults = new List<(string? PointerFile, string? BinaryFile)>
        {
            (null,                              "file1.bin"),           // Binary file without a pointer
            ("file2.bin.pointer.arius",         null),                  // Pointer file without a binary file
            ("file3.bin.pointer.arius",         "file3.bin"),           // Matching binary and pointer files
            (null,                              "folder1/file4.bin"),   // Files with the same name in different directories
            ("folder2/file4.bin.pointer.arius", null),                  // Files with the same name in different directories
            ("folder3/file5.bin.pointer.arius", "folder3/file5.bin")    // Matching binary and pointer files in a folder
        };

        // Act
        var indexedFiles = ArchiveCommandHandler.IndexFiles(fs, Fixture.SourceFolder).ToList();

        // Assert
        var actualResults = indexedFiles
            .Select(fp => (fp.PointerFile?.RelativeNamePlatformNeutral, fp.BinaryFile?.RelativeNamePlatformNeutral)).ToList();

        actualResults.SequenceEqual(expectedResults).Should().BeTrue();
    }
}
