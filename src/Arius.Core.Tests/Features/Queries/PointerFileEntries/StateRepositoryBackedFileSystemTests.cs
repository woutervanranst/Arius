using Arius.Core.Features.Queries.PointerFileEntries;
using Arius.Core.Shared.StateRepositories;
using Arius.Core.Tests.Helpers.Builders;
using Arius.Core.Tests.Helpers.Fakes;
using Arius.Core.Tests.Helpers.Fixtures;
using Shouldly;
using Zio;

namespace Arius.Core.Tests.Features.Queries.PointerFileEntries;

public class StateRepositoryBackedFileSystemTests : IClassFixture<FixtureWithFileSystem>
{
    private readonly FixtureWithFileSystem fixture;
    private readonly StateCache?           stateCache;

    public StateRepositoryBackedFileSystemTests(FixtureWithFileSystem fixture)
    {
        this.fixture = fixture;
        stateCache   = new StateCache(fixture.RepositoryOptions.AccountName, fixture.RepositoryOptions.ContainerName);
    }

    private IStateRepository CreateRepository(StateRepositoryBuilder builder, bool useFakeRepository)
    {
        if (useFakeRepository)
            return builder.BuildFake();
        else
            return builder.Build(stateCache, $"test-state-{Guid.NewGuid()}");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FileExists_WithExistingPointerFileEntry_ReturnsTrue(bool useFakeRepository)
    {
        // Arrange
        var hash = FakeHashBuilder.GenerateValidHash(1);
        var repository = CreateRepository(new StateRepositoryBuilder()
            .WithBinaryProperty(hash, 1024, bp => bp
                .WithPointerFileEntry("/documents/test.txt")), useFakeRepository);

        var fileSystem = new StateRepositoryBackedFileSystem(repository);

        // Act
        var exists = fileSystem.FileExists("/documents/test.txt.pointer.arius");

        // Assert
        exists.ShouldBeTrue();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FileExists_WithNonExistingFile_ReturnsFalse(bool useFakeRepository)
    {
        // Arrange
        var repository = CreateRepository(new StateRepositoryBuilder(), useFakeRepository);
        var fileSystem = new StateRepositoryBackedFileSystem(repository);

        // Act
        var exists = fileSystem.FileExists("/nonexistent.txt");

        // Assert
        exists.ShouldBeFalse();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FileExists_WithRelativePath_ThrowsArgumentException(bool useFakeRepository)
    {
        // Arrange
        var repository = CreateRepository(new StateRepositoryBuilder(), useFakeRepository);
        var fileSystem = new StateRepositoryBackedFileSystem(repository);

        // Act & Assert
        var ex = Should.Throw<ArgumentException>(() => fileSystem.FileExists("relative/path.txt"));
        ex.Message.ShouldContain("must be absolute");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void GetFileLength_WithExistingFile_ReturnsOriginalSize(bool useFakeRepository)
    {
        // Arrange
        var hash         = FakeHashBuilder.GenerateValidHash(1);
        var expectedSize = 2048L;
        var repository = CreateRepository(new StateRepositoryBuilder()
            .WithBinaryProperty(hash, expectedSize, bp => bp
                .WithPointerFileEntry("/documents/large-file.txt")), useFakeRepository);

        var fileSystem = new StateRepositoryBackedFileSystem(repository);

        // Act
        var fileLength = fileSystem.GetFileLength("/documents/large-file.txt.pointer.arius");

        // Assert
        fileLength.ShouldBe(expectedSize);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void GetFileLength_WithNonExistingFile_ThrowsFileNotFoundException(bool useFakeRepository)
    {
        // Arrange
        var repository = CreateRepository(new StateRepositoryBuilder(), useFakeRepository);
        var fileSystem = new StateRepositoryBackedFileSystem(repository);

        // Act & Assert
        Should.Throw<FileNotFoundException>(() => fileSystem.GetFileLength("/nonexistent.txt"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void GetFileLength_WithRelativePath_ThrowsArgumentException(bool useFakeRepository)
    {
        // Arrange
        var repository = CreateRepository(new StateRepositoryBuilder(), useFakeRepository);
        var fileSystem = new StateRepositoryBackedFileSystem(repository);

        // Act & Assert
        var ex = Should.Throw<ArgumentException>(() => fileSystem.GetFileLength("relative/path.txt"));
        ex.Message.ShouldContain("must be absolute");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void GetLastWriteTime_ThrowsNotSupportedException(bool useFakeRepository)
    {
        // Arrange
        var repository = CreateRepository(new StateRepositoryBuilder(), useFakeRepository);
        var fileSystem = new StateRepositoryBackedFileSystem(repository);

        // Act & Assert
        Should.Throw<NotSupportedException>(() => fileSystem.GetLastWriteTime("/test.txt"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void GetCreationTime_ThrowsNotSupportedException(bool useFakeRepository)
    {
        // Arrange
        var repository = CreateRepository(new StateRepositoryBuilder(), useFakeRepository);
        var fileSystem = new StateRepositoryBackedFileSystem(repository);

        // Act & Assert
        Should.Throw<NotSupportedException>(() => fileSystem.GetCreationTime("/test.txt"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EnumeratePaths_WithRootDirectory_ReturnsAllFiles(bool useFakeRepository)
    {
        // Arrange
        var hash1 = FakeHashBuilder.GenerateValidHash(1);
        var hash2 = FakeHashBuilder.GenerateValidHash(2);
        var repository = CreateRepository(new StateRepositoryBuilder()
            .WithBinaryProperty(hash1, 1024, bp => bp
                .WithPointerFileEntry("/documents/file1.txt")
                .WithPointerFileEntry("/documents/file2.txt"))
            .WithBinaryProperty(hash2, 2048, bp => bp
                .WithPointerFileEntry("/images/photo.jpg")), useFakeRepository);

        var fileSystem = new StateRepositoryBackedFileSystem(repository);

        // Act
        var paths = fileSystem.EnumeratePaths("/", "*", SearchOption.AllDirectories, SearchTarget.File).ToList();

        // Assert
        paths.ShouldContain("/documents/file1.txt.pointer.arius");
        paths.ShouldContain("/documents/file2.txt.pointer.arius");
        paths.ShouldContain("/images/photo.jpg.pointer.arius");
        paths.Count.ShouldBe(3);
    }

    [Theory(Skip = "TODO Probably this is not necessary? Remove also the tests")]
    [InlineData(true)]
    [InlineData(false)]
    public void EnumeratePaths_WithSpecificDirectory_ReturnsMatchingFiles(bool useFakeRepository)
    {
        // Arrange
        var hash1 = FakeHashBuilder.GenerateValidHash(1);
        var hash2 = FakeHashBuilder.GenerateValidHash(2);
        var repository = CreateRepository(new StateRepositoryBuilder()
            .WithBinaryProperty(hash1, 1024, bp => bp
                .WithPointerFileEntry("/documents/file1.txt")
                .WithPointerFileEntry("/documents/subfolder/file2.txt"))
            .WithBinaryProperty(hash2, 2048, bp => bp
                .WithPointerFileEntry("/images/photo.jpg")), useFakeRepository);

        var fileSystem = new StateRepositoryBackedFileSystem(repository);

        // Act
        var paths = fileSystem.EnumeratePaths("/documents", "*", SearchOption.TopDirectoryOnly, SearchTarget.File).ToList();

        // Assert
        paths.ShouldContain("/documents/file1.txt.pointer.arius");
        paths.ShouldNotContain("/documents/subfolder/file2.txt.pointer.arius"); // Should be filtered out by TopDirectoryOnly
        paths.ShouldNotContain("/images/photo.jpg.pointer.arius");
        paths.Count.ShouldBe(1);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EnumeratePaths_WithDirectoryTarget_ThrowsNotSupportedException(bool useFakeRepository)
    {
        // Arrange
        var repository = CreateRepository(new StateRepositoryBuilder(), useFakeRepository);
        var fileSystem = new StateRepositoryBackedFileSystem(repository);

        // Act & Assert
        Should.Throw<NotSupportedException>(() =>
            fileSystem.EnumeratePaths("/", "*", SearchOption.AllDirectories, SearchTarget.Directory).ToList());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EnumeratePaths_WithNonWildcardPattern_ThrowsNotSupportedException(bool useFakeRepository)
    {
        // Arrange
        var repository = CreateRepository(new StateRepositoryBuilder(), useFakeRepository);
        var fileSystem = new StateRepositoryBackedFileSystem(repository);

        // Act & Assert
        Should.Throw<NotSupportedException>(() =>
            fileSystem.EnumeratePaths("/", "*.txt", SearchOption.AllDirectories, SearchTarget.File).ToList());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EnumeratePaths_WithRelativePath_ThrowsArgumentException(bool useFakeRepository)
    {
        // Arrange
        var repository = CreateRepository(new StateRepositoryBuilder(), useFakeRepository);
        var fileSystem = new StateRepositoryBackedFileSystem(repository);

        // Act & Assert
        var ex = Should.Throw<ArgumentException>(() =>
            fileSystem.EnumeratePaths("relative/path", "*", SearchOption.AllDirectories, SearchTarget.File).ToList());
        ex.Message.ShouldContain("must be absolute");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void GetAttributes_ReturnsReadOnlyAndNormal(bool useFakeRepository)
    {
        // Arrange
        var hash = FakeHashBuilder.GenerateValidHash(1);
        var repository = CreateRepository(new StateRepositoryBuilder()
            .WithBinaryProperty(hash, 1024, bp => bp
                .WithPointerFileEntry("/test.txt")), useFakeRepository);

        var fileSystem = new StateRepositoryBackedFileSystem(repository);

        // Act
        var attributes = fileSystem.GetAttributes("/test.txt.pointer.arius");

        // Assert
        attributes.ShouldBe(FileAttributes.ReadOnly | FileAttributes.Normal);
    }
}