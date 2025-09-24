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

    [Fact]
    public async Task EnumerateDirectoryEntries_WithTopDirectoryOnly_ShouldListOnlyTopLevelNonExcludedDirectories()
    {
        // Arrange
        var expectedRelativePaths = new[]
        {
            "/subdir",
            "/subdir2"
        };

        // Act
        var dirs = fixture.FileSystem.EnumerateDirectoryEntries(UPath.Root, "*", SearchOption.TopDirectoryOnly).ToList();

        // Assert
        dirs.ShouldNotBeNull();
        dirs.Select(de => de.FullName)
            .ShouldBe(expectedRelativePaths, ignoreOrder: true);
    }

    [Fact]
    public async Task EnumerateDirectoryEntries_WithAllDirectories_ShouldSkipExcludedDirectories()
    {
        // Arrange
        // NB: The fixture includes the excluded directories under /subdir2:
        // @eaDir, eaDir, SynoResource � these must be skipped.
        var expectedRelativePaths = new[]
        {
            "/subdir",
            "/subdir2"
        };

        // Act
        var dirs = fixture.FileSystem.EnumerateDirectoryEntries(UPath.Root, "*", SearchOption.AllDirectories).ToList();

        // Assert
        dirs.ShouldNotBeNull();
        dirs.Select(de => de.FullName)
            .ShouldBe(expectedRelativePaths, ignoreOrder: true);
    }

    [Fact]
    public async Task EnumerateDirectoryEntries_WithTopDirectoryOnly_OnSubdir2_ShouldReturnNoNonExcludedChildren()
    {
        // Arrange
        // /subdir2 only contains excluded children (@eaDir, eaDir, SynoResource) in the fixture.
        var expectedRelativePaths = Array.Empty<string>();

        // Act
        var dirs = fixture.FileSystem.EnumerateDirectoryEntries(new UPath("/subdir2"), "*", SearchOption.TopDirectoryOnly).ToList();

        // Assert
        dirs.ShouldNotBeNull();
        dirs.Select(de => de.FullName)
            .ShouldBe(expectedRelativePaths, ignoreOrder: true);
    }

    [Fact(Skip = "Properly implement leading dot in files & folders")]
    public async Task EnumerateDirectoryEntries_ShouldSkipHiddenAndSystemDirectories()
    {
        // Arrange
        // Create a hidden directory and a normal directory under root.
        var hiddenDir = new UPath("/.hidden_dir");     // leading dot = hidden (Linux convention)
        var normalDir = new UPath("/visible_dir");

        fixture.FileSystem.CreateDirectory(normalDir);
        // Mark hidden dir explicitly hidden via attributes if supported by the fake FS, otherwise
        // creating a directory with leading dot should be sufficient for ShouldSkipDirectory name/attr checks.
        fixture.FileSystem.CreateDirectory(hiddenDir);
        // If your Fake FS supports setting attributes on directories, uncomment the next line:
        // fixture.FileSystem.SetAttributes(hiddenDir, FileAttributes.Directory | FileAttributes.Hidden);

        var expectedRelativePaths = new[]
        {
            "/subdir",
            "/subdir2",
            "/visible_dir"
        };

        // Act
        var dirs = fixture.FileSystem.EnumerateDirectoryEntries(UPath.Root, "*", SearchOption.TopDirectoryOnly).ToList();

        // Assert
        dirs.ShouldNotBeNull();
        dirs.Select(de => de.FullName)
            .ShouldBe(expectedRelativePaths, ignoreOrder: true);
    }

    [Fact]
    public async Task EnumerateDirectoryEntries_WithNonWildcardPattern_ShouldThrow()
    {
        // Act & Assert
        var ex = Should.Throw<NotSupportedException>(() =>
        {
            // Any non-"*" pattern should throw per implementation
            fixture.FileSystem.EnumerateDirectoryEntries(UPath.Root, "*.txt", SearchOption.TopDirectoryOnly).ToList();
        });

        ex.ShouldNotBeNull();
    }

    [Fact]
    public async Task EnumerateFileEntries_WithNonExistentPath_ShouldReturnEmptyCollection()
    {
        // Arrange
        var nonExistentPath = new UPath("/path/that/does/not/exist");

        // Act
        var files = fixture.FileSystem.EnumerateFileEntries(nonExistentPath, "*", SearchOption.TopDirectoryOnly).ToList();

        // Assert
        files.ShouldNotBeNull();
        files.ShouldBeEmpty();
    }
}