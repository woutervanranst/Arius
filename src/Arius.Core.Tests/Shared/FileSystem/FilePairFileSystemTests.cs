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
    }

    [Fact]
    public async Task EnumerateFileEntries_ShouldEnumerateFilePairsButSkipHiddenFilesAndDirectories()
    {
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
        new FakeFileBuilder(fixture).WithActualFile(FilePairType.BinaryFileOnly, "/file1.txt").WithRandomContent(0).WithAttributes(FileAttributes.Normal).Build(); // Root level file
        new FakeFileBuilder(fixture).WithActualFile(FilePairType.BinaryFileOnly, "/file2.txt").WithRandomContent(0).WithAttributes(FileAttributes.Normal).Build(); // Another root level file
        new FakeFileBuilder(fixture).WithActualFile(FilePairType.BinaryFileOnly, "/subdir/file3.txt").WithRandomContent(0).WithAttributes(FileAttributes.Normal).Build(); // File in subdirectory - should be excluded
        new FakeFileBuilder(fixture).WithActualFile(FilePairType.BinaryFileOnly, "/subdir/file4.txt").WithRandomContent(0).WithAttributes(FileAttributes.Normal).Build(); // Another file in subdirectory - should be excluded
        new FakeFileBuilder(fixture).WithActualFile(FilePairType.BinaryFileOnly, "/subdir2/file5.txt").WithRandomContent(0).WithAttributes(FileAttributes.Normal).Build(); // File in another subdirectory - should be excluded

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
    public async Task EnumerateFileEntries_WithTopDirectoryOnly_ShouldHandlePointerFiles()
    {
        // Arrange
        new FakeFileBuilder(fixture).WithActualFile(FilePairType.BinaryFileWithPointerFile, "/file1.txt").WithRandomContent(0).WithAttributes(FileAttributes.Normal).Build(); // Root level binary file with pointer
        new FakeFileBuilder(fixture).WithActualFile(FilePairType.PointerFileOnly, "/file2.txt").WithRandomContent(0).WithAttributes(FileAttributes.Normal).Build(); // Root level pointer file only
        new FakeFileBuilder(fixture).WithActualFile(FilePairType.BinaryFileOnly, "/file3.txt").WithRandomContent(0).WithAttributes(FileAttributes.Normal).Build(); // Root level binary file only
        new FakeFileBuilder(fixture).WithActualFile(FilePairType.BinaryFileWithPointerFile, "/subdir/file4.txt").WithRandomContent(0).WithAttributes(FileAttributes.Normal).Build(); // Subdirectory file - should be excluded
        new FakeFileBuilder(fixture).WithActualFile(FilePairType.PointerFileOnly, "/subdir/file5.txt").WithRandomContent(0).WithAttributes(FileAttributes.Normal).Build(); // Subdirectory pointer - should be excluded

        var expectedRelativePaths = new[]
        {
            "/file1.txt", // Binary file with pointer
            "/file2.txt", // Pointer file only (returns binary path)
            "/file3.txt"  // Binary file only
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
        // Arrange - Create files in various directories
        new FakeFileBuilder(fixture).WithActualFile(FilePairType.BinaryFileOnly, "/rootfile.txt").WithRandomContent(0).WithAttributes(FileAttributes.Normal).Build(); // Root level - should be excluded
        new FakeFileBuilder(fixture).WithActualFile(FilePairType.BinaryFileOnly, "/targetdir/file1.txt").WithRandomContent(0).WithAttributes(FileAttributes.Normal).Build(); // Target directory file
        new FakeFileBuilder(fixture).WithActualFile(FilePairType.BinaryFileOnly, "/targetdir/file2.txt").WithRandomContent(0).WithAttributes(FileAttributes.Normal).Build(); // Another target directory file
        new FakeFileBuilder(fixture).WithActualFile(FilePairType.BinaryFileWithPointerFile, "/targetdir/file3.txt").WithRandomContent(0).WithAttributes(FileAttributes.Normal).Build(); // Binary file with pointer in target directory
        new FakeFileBuilder(fixture).WithActualFile(FilePairType.BinaryFileOnly, "/targetdir/subdir/file4.txt").WithRandomContent(0).WithAttributes(FileAttributes.Normal).Build(); // Nested subdirectory - should be excluded
        new FakeFileBuilder(fixture).WithActualFile(FilePairType.BinaryFileOnly, "/otherdir/file5.txt").WithRandomContent(0).WithAttributes(FileAttributes.Normal).Build(); // Different directory - should be excluded

        var expectedRelativePaths = new[]
        {
            "/targetdir/file1.txt",
            "/targetdir/file2.txt",
            "/targetdir/file3.txt"
        };

        // Act - Enumerate files in /targetdir with TopDirectoryOnly
        var files = fixture.FileSystem.EnumerateFileEntries(new UPath("/targetdir"), "*", SearchOption.TopDirectoryOnly).ToList();

        // Assert
        files.ShouldNotBeNull();
        files.Select(fe => fe.FullName)
            .ShouldBe(expectedRelativePaths, ignoreOrder: true);
    }
}