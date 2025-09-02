using Arius.Core.Shared.FileSystem;
using Shouldly;
using Zio;

namespace Arius.Core.Tests.Models;

public class FilePairFileSystemTests : IClassFixture<Fixture>
{
    private readonly Fixture fixture;

    public FilePairFileSystemTests(Fixture fixture)
    {
        this.fixture = fixture;
    }

    [Fact]
    public async Task EnumerateFileEntries_ShouldEnumerateFilePairsButSkipHiddenFilesAndDirectories()
    {
        fixture.GivenSourceFolderHavingFilePair("/file1.txt",                      FilePairType.BinaryFileOnly,            0, attributes: FileAttributes.Normal); // Normal file
        fixture.GivenSourceFolderHavingFilePair("/file2.txt",                      FilePairType.BinaryFileOnly,            0, attributes: FileAttributes.Normal); // Another normal file
        fixture.GivenSourceFolderHavingFilePair("/subdir/file3.txt",               FilePairType.BinaryFileOnly,            0, attributes: FileAttributes.Normal); // Normal file in a subdirectory
        fixture.GivenSourceFolderHavingFilePair("/subdir/file4.txt",               FilePairType.BinaryFileOnly,            0, attributes: FileAttributes.Normal); // Another normal file in a subdirectory
        fixture.GivenSourceFolderHavingFilePair("/subdir/file5.txt",               FilePairType.BinaryFileWithPointerFile, 0, attributes: FileAttributes.Normal); // A BinaryFile + PointerFile pair
        fixture.GivenSourceFolderHavingFilePair("/.file5_hidden.txt",              FilePairType.BinaryFileOnly,            0, attributes: FileAttributes.Hidden); // Hidden file - Linux convention means leading dot
        fixture.GivenSourceFolderHavingFilePair("/subdir2/@eaDir/file6.txt",       FilePairType.BinaryFileOnly,            0, attributes: FileAttributes.Normal); // File in excluded directory
        fixture.GivenSourceFolderHavingFilePair("/subdir2/eaDir/file7.txt",        FilePairType.BinaryFileOnly,            0, attributes: FileAttributes.Normal); // Another file in excluded directory
        fixture.GivenSourceFolderHavingFilePair("/subdir2/SynoResource/file8.txt", FilePairType.BinaryFileOnly,            0, attributes: FileAttributes.Normal); // File in another excluded directory
        fixture.GivenSourceFolderHavingFilePair("/subdir2/file9.txt",              FilePairType.BinaryFileOnly,            0, attributes: FileAttributes.Normal); // Normal file
        fixture.GivenSourceFolderHavingFilePair("/subdir2/AuToRuN.ini",            FilePairType.BinaryFileOnly,            0, attributes: FileAttributes.Normal); // Excluded file by name
        fixture.GivenSourceFolderHavingFilePair("/subdir2/thumbs.db",              FilePairType.BinaryFileOnly,            0, attributes: FileAttributes.Normal); // Excluded file by name
        fixture.GivenSourceFolderHavingFilePair("/subdir2/.ds_store",              FilePairType.BinaryFileOnly,            0, attributes: FileAttributes.Normal); // Excluded file by name

        var expectedRelativePaths = new[]
        {
            "/file1.txt",
            "/file2.txt",
            "/subdir/file3.txt",
            "/subdir/file4.txt",
            "/subdir/file5.txt",
            "/subdir2/file9.txt"
        };

        // Act
        var files = fixture.FileSystem.EnumerateFileEntries(UPath.Root, "*", SearchOption.AllDirectories).ToList();

        // Assert
        files.ShouldNotBeNull();
        files.Select(fe => fe.FullName)
            .ShouldBe(expectedRelativePaths, ignoreOrder: true);
    }
}