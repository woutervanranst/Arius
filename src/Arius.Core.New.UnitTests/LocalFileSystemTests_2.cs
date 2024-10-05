using Arius.Core.Domain.Storage.FileSystem;
using Arius.Core.Infrastructure.Storage.LocalFileSystem;
using Arius.Core.New.UnitTests.Fixtures;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Arius.Core.New.UnitTests;

public class LocalFileSystemTests_2 : TestBase
{
    protected override AriusFixture GetFixture()
    {
        return new FixtureBuilder()
            .WithRealStorageAccountFactory()
            .WithUniqueContainerName().Build();
    }

    protected override void ConfigureOnceForFixture()
    {
        GivenSourceFolderHavingFilePair("file1.bin",                       FilePairType.BinaryFileOnly,            0, FileAttributes.Normal); // Binary file without a pointer
        GivenSourceFolderHavingFilePair("file2.bin.pointer.arius",         FilePairType.PointerFileOnly,           0, FileAttributes.Normal); // Pointer file without a binary file
        GivenSourceFolderHavingFilePair("file3.bin",                       FilePairType.BinaryFileWithPointerFile, 0, FileAttributes.Normal); // Binary file with a matching pointer file
        GivenSourceFolderHavingFilePair("folder1/file4.bin",               FilePairType.BinaryFileOnly,            0, FileAttributes.Normal); // Binary file in a folder
        GivenSourceFolderHavingFilePair("folder2/file4.bin.pointer.arius", FilePairType.PointerFileOnly,           0, FileAttributes.Normal); // Pointer file with the same name in another folder
        GivenSourceFolderHavingFilePair("folder3/file5.bin",               FilePairType.BinaryFileWithPointerFile, 0, FileAttributes.Normal);
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
        var indexedFiles = fs.EnumerateFilePairs(Fixture.TestRunSourceFolder).ToList();

        // Assert
        var actualResults = indexedFiles
            .Select(fp => fp.Type switch
            {
                FilePairType.PointerFileOnly           => (fp.PointerFile?.RelativeNamePlatformNeutral, null),
                FilePairType.BinaryFileOnly            => (null, fp.BinaryFile?.RelativeNamePlatformNeutral),
                FilePairType.BinaryFileWithPointerFile => (fp.PointerFile?.RelativeNamePlatformNeutral, fp.BinaryFile?.RelativeNamePlatformNeutral),
                _                                      => throw new ArgumentOutOfRangeException()
            }).ToList();

        actualResults.Should().BeEquivalentTo(expectedResults);
    }
}