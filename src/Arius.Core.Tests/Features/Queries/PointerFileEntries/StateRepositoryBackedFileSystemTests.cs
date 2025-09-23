using Arius.Core.Features.Queries.PointerFileEntries;
using Arius.Core.Tests.Helpers.Builders;
using Arius.Core.Tests.Helpers.Fakes;
using Shouldly;
using Zio;

namespace Arius.Core.Tests.Features.Queries.PointerFileEntries;

public class StateRepositoryBackedFileSystemTests
{
    [Fact]
    public void FileExists_WithExistingPointerFileEntry_ReturnsTrue()
    {
        // Arrange
        var hash = FakeHashBuilder.GenerateValidHash(1);
        var repository = new StateRepositoryBuilder()
            .WithBinaryProperty(hash, 1024, bp => bp
                .WithPointerFileEntry("/documents/test.txt"))
            .BuildFake();

        var fileSystem = new StateRepositoryBackedFileSystem(repository);

        // Act
        var exists = fileSystem.FileExists("/documents/test.txt.pointer.arius");

        // Assert
        exists.ShouldBeTrue();
    }

    [Fact]
    public void FileExists_WithNonExistingFile_ReturnsFalse()
    {
        // Arrange
        var repository = new StateRepositoryBuilder().BuildFake();
        var fileSystem = new StateRepositoryBackedFileSystem(repository);

        // Act
        var exists = fileSystem.FileExists("/nonexistent.txt");

        // Assert
        exists.ShouldBeFalse();
    }

    [Fact]
    public void FileExists_WithRelativePath_ThrowsArgumentException()
    {
        // Arrange
        var repository = new StateRepositoryBuilder().BuildFake();
        var fileSystem = new StateRepositoryBackedFileSystem(repository);

        // Act & Assert
        Should.Throw<ArgumentException>(() => fileSystem.FileExists("relative/path.txt"));
    }

    [Fact]
    public void GetFileLength_WithExistingFile_ReturnsOriginalSize()
    {
        // Arrange
        var hash = FakeHashBuilder.GenerateValidHash(1);
        var expectedSize = 2048L;
        var repository = new StateRepositoryBuilder()
            .WithBinaryProperty(hash, expectedSize, bp => bp
                .WithPointerFileEntry("/documents/large-file.txt"))
            .BuildFake();

        var fileSystem = new StateRepositoryBackedFileSystem(repository);

        // Act
        var fileLength = fileSystem.GetFileLength("/documents/large-file.txt.pointer.arius");

        // Assert
        fileLength.ShouldBe(expectedSize);
    }

    [Fact]
    public void GetFileLength_WithNonExistingFile_ThrowsFileNotFoundException()
    {
        // Arrange
        var repository = new StateRepositoryBuilder().BuildFake();
        var fileSystem = new StateRepositoryBackedFileSystem(repository);

        // Act & Assert
        Should.Throw<FileNotFoundException>(() => fileSystem.GetFileLength("/nonexistent.txt"));
    }

    [Fact]
    public void GetFileLength_WithRelativePath_ThrowsArgumentException()
    {
        // Arrange
        var repository = new StateRepositoryBuilder().BuildFake();
        var fileSystem = new StateRepositoryBackedFileSystem(repository);

        // Act & Assert
        Should.Throw<ArgumentException>(() => fileSystem.GetFileLength("relative/path.txt"));
    }

    [Fact]
    public void GetLastWriteTime_ThrowsNotSupportedException()
    {
        // Arrange
        var repository = new StateRepositoryBuilder().BuildFake();
        var fileSystem = new StateRepositoryBackedFileSystem(repository);

        // Act & Assert
        Should.Throw<NotSupportedException>(() => fileSystem.GetLastWriteTime("/test.txt"));
    }

    [Fact]
    public void GetCreationTime_ThrowsNotSupportedException()
    {
        // Arrange
        var repository = new StateRepositoryBuilder().BuildFake();
        var fileSystem = new StateRepositoryBackedFileSystem(repository);

        // Act & Assert
        Should.Throw<NotSupportedException>(() => fileSystem.GetCreationTime("/test.txt"));
    }

    [Fact]
    public void EnumeratePaths_WithRootDirectory_ReturnsAllFiles()
    {
        // Arrange
        var hash1 = FakeHashBuilder.GenerateValidHash(1);
        var hash2 = FakeHashBuilder.GenerateValidHash(2);
        var repository = new StateRepositoryBuilder()
            .WithBinaryProperty(hash1, 1024, bp => bp
                .WithPointerFileEntry("/documents/file1.txt")
                .WithPointerFileEntry("/documents/file2.txt"))
            .WithBinaryProperty(hash2, 2048, bp => bp
                .WithPointerFileEntry("/images/photo.jpg"))
            .BuildFake();

        var fileSystem = new StateRepositoryBackedFileSystem(repository);

        // Act
        var paths = fileSystem.EnumeratePaths("/", "*", SearchOption.AllDirectories, SearchTarget.File).ToList();

        // Assert
        paths.ShouldContain("/documents/file1.txt.pointer.arius");
        paths.ShouldContain("/documents/file2.txt.pointer.arius");
        paths.ShouldContain("/images/photo.jpg.pointer.arius");
        paths.Count.ShouldBe(3);
    }

    [Fact]
    public void EnumeratePaths_WithSpecificDirectory_ReturnsMatchingFiles()
    {
        // Arrange
        var hash1 = FakeHashBuilder.GenerateValidHash(1);
        var hash2 = FakeHashBuilder.GenerateValidHash(2);
        var repository = new StateRepositoryBuilder()
            .WithBinaryProperty(hash1, 1024, bp => bp
                .WithPointerFileEntry("/documents/file1.txt")
                .WithPointerFileEntry("/documents/subfolder/file2.txt"))
            .WithBinaryProperty(hash2, 2048, bp => bp
                .WithPointerFileEntry("/images/photo.jpg"))
            .BuildFake();

        var fileSystem = new StateRepositoryBackedFileSystem(repository);

        // Act
        var paths = fileSystem.EnumeratePaths("/documents", "*", SearchOption.TopDirectoryOnly, SearchTarget.File).ToList();

        // Assert
        paths.ShouldContain("/documents/file1.txt.pointer.arius");
        paths.ShouldNotContain("/documents/subfolder/file2.txt.pointer.arius"); // Should be filtered out by TopDirectoryOnly
        paths.ShouldNotContain("/images/photo.jpg.pointer.arius");
        paths.Count.ShouldBe(1);
    }

    [Fact]
    public void EnumeratePaths_WithDirectoryTarget_ThrowsNotSupportedException()
    {
        // Arrange
        var repository = new StateRepositoryBuilder().BuildFake();
        var fileSystem = new StateRepositoryBackedFileSystem(repository);

        // Act & Assert
        Should.Throw<NotSupportedException>(() =>
            fileSystem.EnumeratePaths("/", "*", SearchOption.AllDirectories, SearchTarget.Directory).ToList());
    }

    [Fact]
    public void EnumeratePaths_WithNonWildcardPattern_ThrowsNotSupportedException()
    {
        // Arrange
        var repository = new StateRepositoryBuilder().BuildFake();
        var fileSystem = new StateRepositoryBackedFileSystem(repository);

        // Act & Assert
        Should.Throw<NotSupportedException>(() =>
            fileSystem.EnumeratePaths("/", "*.txt", SearchOption.AllDirectories, SearchTarget.File).ToList());
    }

    [Fact]
    public void GetAttributes_ReturnsReadOnlyAndNormal()
    {
        // Arrange
        var hash = FakeHashBuilder.GenerateValidHash(1);
        var repository = new StateRepositoryBuilder()
            .WithBinaryProperty(hash, 1024, bp => bp
                .WithPointerFileEntry("/test.txt"))
            .BuildFake();

        var fileSystem = new StateRepositoryBackedFileSystem(repository);

        // Act
        var attributes = fileSystem.GetAttributes("/test.txt.pointer.arius");

        // Assert
        attributes.ShouldBe(FileAttributes.ReadOnly | FileAttributes.Normal);
    }
}