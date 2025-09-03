using Arius.Core.Shared.FileSystem;
using Arius.Core.Tests.Helpers.Fakes;
using Arius.Core.Tests.Helpers.Fixtures;
using Shouldly;
using Zio;

namespace Arius.Core.Tests.Shared.FileSystem;

public class FilePairFileSystemTests : IClassFixture<PhysicalFileSystemFixture>
{
    private readonly PhysicalFileSystemFixture fixture;

    public FilePairFileSystemTests(PhysicalFileSystemFixture fixture)
    {
        this.fixture = fixture;
    }

    [Fact]
    public async Task EnumerateFileEntries_ShouldEnumerateFilePairsButSkipHiddenFilesAndDirectories()
    {
        fixture.FileSystem.WithSourceFolderHavingFilePair("/file1.txt",                      FilePairType.BinaryFileOnly,            0, attributes: FileAttributes.Normal); // Normal file
        fixture.FileSystem.WithSourceFolderHavingFilePair("/file2.txt",                      FilePairType.BinaryFileOnly,            0, attributes: FileAttributes.Normal); // Another normal file
        fixture.FileSystem.WithSourceFolderHavingFilePair("/subdir/file3.txt",               FilePairType.BinaryFileOnly,            0, attributes: FileAttributes.Normal); // Normal file in a subdirectory
        fixture.FileSystem.WithSourceFolderHavingFilePair("/subdir/file4.txt",               FilePairType.BinaryFileOnly,            0, attributes: FileAttributes.Normal); // Another normal file in a subdirectory
        fixture.FileSystem.WithSourceFolderHavingFilePair("/subdir/file5.txt",               FilePairType.BinaryFileWithPointerFile, 0, attributes: FileAttributes.Normal); // A BinaryFile + PointerFile pair
        fixture.FileSystem.WithSourceFolderHavingFilePair("/.file5_hidden.txt",              FilePairType.BinaryFileOnly,            0, attributes: FileAttributes.Hidden); // Hidden file - Linux convention means leading dot
        fixture.FileSystem.WithSourceFolderHavingFilePair("/subdir2/@eaDir/file6.txt",       FilePairType.BinaryFileOnly,            0, attributes: FileAttributes.Normal); // File in excluded directory
        fixture.FileSystem.WithSourceFolderHavingFilePair("/subdir2/eaDir/file7.txt",        FilePairType.BinaryFileOnly,            0, attributes: FileAttributes.Normal); // Another file in excluded directory
        fixture.FileSystem.WithSourceFolderHavingFilePair("/subdir2/SynoResource/file8.txt", FilePairType.BinaryFileOnly,            0, attributes: FileAttributes.Normal); // File in another excluded directory
        fixture.FileSystem.WithSourceFolderHavingFilePair("/subdir2/file9.txt",              FilePairType.BinaryFileOnly,            0, attributes: FileAttributes.Normal); // Normal file
        fixture.FileSystem.WithSourceFolderHavingFilePair("/subdir2/AuToRuN.ini",            FilePairType.BinaryFileOnly,            0, attributes: FileAttributes.Normal); // Excluded file by name
        fixture.FileSystem.WithSourceFolderHavingFilePair("/subdir2/thumbs.db",              FilePairType.BinaryFileOnly,            0, attributes: FileAttributes.Normal); // Excluded file by name
        fixture.FileSystem.WithSourceFolderHavingFilePair("/subdir2/.ds_store",              FilePairType.BinaryFileOnly,            0, attributes: FileAttributes.Normal); // Excluded file by name

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