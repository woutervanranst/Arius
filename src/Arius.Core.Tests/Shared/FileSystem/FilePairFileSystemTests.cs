using Arius.Core.Shared.FileSystem;
using Arius.Core.Tests.Helpers.Fakes;
using Arius.Core.Tests.Helpers.Fixtures;
using Shouldly;
using Zio;

namespace Arius.Core.Tests.Shared.FileSystem;

public class FilePairFileSystemTests
{
    private readonly FixtureWithFileSystem fixture;

    public FilePairFileSystemTests()
    {
        this.fixture = new();

        new FakeFileBuilder(fixture).WithActualFile(FilePairType.BinaryFileOnly, "/file1.txt").WithRandomContent(0).WithAttributes(FileAttributes.Normal).Build(); // Normal file
        new FakeFileBuilder(fixture).WithActualFile(FilePairType.BinaryFileOnly, "/file2.txt").WithRandomContent(0).WithAttributes(FileAttributes.Normal).Build(); // Another normal file
        new FakeFileBuilder(fixture).WithActualFile(FilePairType.BinaryFileOnly, "/subdir/file3.txt").WithRandomContent(0).WithAttributes(FileAttributes.Normal).Build(); // Normal file in a subdirectory
        new FakeFileBuilder(fixture).WithActualFile(FilePairType.BinaryFileOnly, "/subdir/file4.txt").WithRandomContent(0).WithAttributes(FileAttributes.Normal).Build(); // Another normal file in a subdirectory
        new FakeFileBuilder(fixture).WithActualFile(FilePairType.BinaryFileWithPointerFile, "/subdir/file5.txt").WithRandomContent(0).WithAttributes(FileAttributes.Normal).Build(); // A BinaryFile + PointerFile pair
        new FakeFileBuilder(fixture).WithActualFile(FilePairType.BinaryFileOnly, "/.file5_hidden.txt").WithRandomContent(0).WithAttributes(FileAttributes.Hidden).Build(); // Hidden file - Linux convention means leading dot
        new FakeFileBuilder(fixture).WithActualFile(FilePairType.BinaryFileOnly, "/subdir2/@eaDir/file6.txt").WithRandomContent(0).WithAttributes(FileAttributes.Normal).Build(); // File in excluded directory
        new FakeFileBuilder(fixture).WithActualFile(FilePairType.BinaryFileOnly, "/subdir2/eaDir/file7.txt").WithRandomContent(0).WithAttributes(FileAttributes.Normal).Build(); // Another file in excluded directory
        new FakeFileBuilder(fixture).WithActualFile(FilePairType.BinaryFileOnly, "/subdir2/SynoResource/file8.txt").WithRandomContent(0).WithAttributes(FileAttributes.Normal).Build(); // File in another excluded directory
        new FakeFileBuilder(fixture).WithActualFile(FilePairType.BinaryFileOnly, "/subdir2/file9.txt").WithRandomContent(0).WithAttributes(FileAttributes.Normal).Build(); // Normal file
        new FakeFileBuilder(fixture).WithActualFile(FilePairType.BinaryFileOnly, "/subdir2/AuToRuN.ini").WithRandomContent(0).WithAttributes(FileAttributes.Normal).Build(); // Excluded file by name
        new FakeFileBuilder(fixture).WithActualFile(FilePairType.BinaryFileOnly, "/subdir2/thumbs.db").WithRandomContent(0).WithAttributes(FileAttributes.Normal).Build(); // Excluded file by name
        new FakeFileBuilder(fixture).WithActualFile(FilePairType.BinaryFileOnly, "/subdir2/.ds_store").WithRandomContent(0).WithAttributes(FileAttributes.Normal).Build(); // Excluded file by name
    }

    [Fact]
    public async Task EnumerateFileEntries_ShouldEnumerateFilePairsButSkipHiddenFilesAndDirectories()
    {
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

    [Fact]
    public async Task EnumerateFileEntries_WithTopDirectoryOnly_ShouldOnlyEnumerateFilesInTopDirectory()
    {
        // Arrange
        var expectedRelativePaths = new[]
        {
            "/file1.txt",
            "/file2.txt"
        };

        // Act
        var files = fixture.FileSystem.EnumerateFileEntries(UPath.Root, "*", SearchOption.TopDirectoryOnly).ToList();

        // Assert
        files.ShouldNotBeNull();
        files.Select(fe => fe.FullName)
            .ShouldBe(expectedRelativePaths, ignoreOrder: true);
    }

    [Fact]
    public async Task EnumerateFileEntries_WithTopDirectoryOnly_ShouldEnumerateSubdirectoryOnly()
    {
        // Arrange
        var expectedRelativePaths = new[]
        {
            "/subdir2/file9.txt"
        };

        // Act - Enumerate files in /subdir2 with TopDirectoryOnly
        var files = fixture.FileSystem.EnumerateFileEntries(new UPath("/subdir2"), "*", SearchOption.TopDirectoryOnly).ToList();

        // Assert
        files.ShouldNotBeNull();
        files.Select(fe => fe.FullName)
            .ShouldBe(expectedRelativePaths, ignoreOrder: true);
    }
}