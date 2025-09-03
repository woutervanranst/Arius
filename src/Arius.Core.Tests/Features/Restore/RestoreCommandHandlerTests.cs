using Arius.Core.Features.Restore;
using Arius.Core.Shared.Hashing;
using Arius.Core.Shared.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
using NSubstitute;
using System.Security.Cryptography;
using System.Text;
using Arius.Core.Tests.Helpers.Builders;
using Zio.FileSystems;

namespace Arius.Core.Tests.Features.Restore;

public class RestoreCommandHandlerTests : IClassFixture<Fixture>
{
    private readonly Fixture                           fixture;
    private readonly FakeLogger<RestoreCommandHandler> logger;
    private readonly RestoreCommandHandler             handler;

    public RestoreCommandHandlerTests(Fixture fixture)
    {
        this.fixture = fixture;
        logger       = new();
        handler      = new RestoreCommandHandler(logger, NullLoggerFactory.Instance, fixture.AriusConfiguration);
    }

    private static Hash GenerateValidHash(string seed)
    {
        var seedBytes = Encoding.UTF8.GetBytes(seed);
        var hashBytes = SHA256.HashData(seedBytes);
        return Hash.FromBytes(hashBytes);
    }

    [Fact]
    public async Task Restore_OnePointerFile_CreateOrOverwritePointerFileOnDisk()
    {
        // Arrange
        var command = new RestoreCommandBuilder(fixture)
            .WithLocalRoot(fixture.TestRunSourceFolder)
            .WithContainerName("test")
            .WithPassphrase("woutervr")
            .WithTargets("./IMG20250126195020.jpg", "./Sam/")
            .Build();

        // TODO directory without trailing /

        // Act
        var result = await handler.Handle(command, CancellationToken.None);
        
        // Assert
        // Should create or overwrite the pointer file on disk
        //true.ShouldBe(false, "Test not implemented");
    }

    [Fact]
    public async Task Bla()
    {
        var command = new RestoreCommandBuilder(fixture)
            .WithLocalRoot(fixture.TestRunSourceFolder)
            .WithContainerName("test")
            .WithPassphrase("woutervr")
            .WithTargets("./file1.jpg", "./Sam/")
            .Build();

        var bs = Substitute.For<IArchiveStorage>();
        bs.ContainerExistsAsync().Returns(Task.FromResult(true));

        var sr = new StateRepositoryBuilder()
            .WithBinaryProperty(GenerateValidHash("file1-hash"), 1)
            .WithPointerFileEntry("/file1.jpg")
            .WithBinaryProperty(GenerateValidHash("file2-hash"), 1)
            .WithPointerFileEntry("/Sam/file2.jpg")
            .WithPointerFileEntry("/Sam/file2-duplicate.jpg")
            .BuildFake();

        //var sr = Substitute.For<IStateRepository>();
        //sr.GetPointerFileEntries(Arg.Any<string>(), true).Returns()

        using var mfs = new MemoryFileSystem();

        var hc = await new HandlerContextBuilder(command)
            .WithBlobStorage(bs)
            .WithStateRepository(sr)
            .WithBaseFileSystem(mfs)
            .BuildAsync();

        var result = await handler.Handle(hc, CancellationToken.None);

    }

    //[Fact]
    //public async Task Restore_MultiplePointerFiles_CreateOrOverwritePointerFilesOnDisk()
    //{
    //    // Arrange
    //    var command = new RestoreCommandBuilder(fixture)
    //        .WithTargets("test-pointer-file1.txt.pointer.arius", "test-pointer-file2.txt.pointer.arius")
    //        .Build();
        
    //    // Act
    //    var result = await handler.Handle(command, CancellationToken.None);
        
    //    // Assert
    //    // Should create or overwrite the pointer files on disk
    //    true.ShouldBe(false, "Test not implemented");
    //}

    //[Fact]
    //public async Task Restore_OneBinaryFile_CreateOrOverwritePointerFileOnDisk()
    //{
    //    // Arrange
    //    var command = new RestoreCommandBuilder(fixture)
    //        .WithTargets("test-binary-file.txt")
    //        .Build();
        
    //    // Act
    //    var result = await handler.Handle(command, CancellationToken.None);
        
    //    // Assert
    //    // Should create or overwrite the pointer file on disk
    //    true.ShouldBe(false, "Test not implemented");
    //}

    //[Fact]
    //public async Task Restore_MultipleBinaryFiles_CreateOrOverwritePointerFilesOnDisk()
    //{
    //    // Arrange
    //    var command = new RestoreCommandBuilder(fixture)
    //        .WithTargets("test-binary-file1.txt", "test-binary-file2.txt")
    //        .Build();
        
    //    // Act
    //    var result = await handler.Handle(command, CancellationToken.None);
        
    //    // Assert
    //    // Should create or overwrite the pointer files on disk
    //    true.ShouldBe(false, "Test not implemented");
    //}

    //[Fact]
    //public async Task Restore_EmptyDirectory_RestoreAllPointerFiles()
    //{
    //    // Arrange
    //    //var command = new RestoreCommandBuilder(fixture)
    //    //    .WithTargets("empty-directory")
    //    //    .Build();
    //    var command = new RestoreCommandBuilder(fixture)
    //        .WithContainerName("test")
    //        .WithTargets("/Sam/")
    //        .Build();

    //    // Act
    //    var result = await handler.Handle(command, CancellationToken.None);
        
    //    // Assert
    //    // Should restore all pointer files in the empty directory
    //    true.ShouldBe(false, "Test not implemented");
    //}

    //[Fact]
    //public async Task Restore_NonEmptyDirectory_RestoreAllPointerFiles()
    //{
    //    // Arrange
    //    var command = new RestoreCommandBuilder(fixture)
    //        .WithTargets("non-empty-directory")
    //        .Build();
        
    //    // Act
    //    var result = await handler.Handle(command, CancellationToken.None);
        
    //    // Assert
    //    // Should restore all pointer files in the non-empty directory
    //    true.ShouldBe(false, "Test not implemented");
    //}

    //[Fact]
    //public async Task RestoreWithDownload_OnePointerFile_RestoreRespectiveBinaryFile()
    //{
    //    // Arrange
    //    var command = new RestoreCommandBuilder(fixture)
    //        .WithTargets("test-pointer-file.txt.pointer.arius")
    //        .WithDownload(true)
    //        .Build();
        
    //    // Act
    //    var result = await handler.Handle(command, CancellationToken.None);
        
    //    // Assert
    //    // Should restore the respective binary file
    //    true.ShouldBe(false, "Test not implemented");
    //}

    //[Fact]
    //public async Task RestoreWithDownload_MultiplePointerFiles_RestoreRespectiveBinaryFiles()
    //{
    //    // Arrange
    //    var command = new RestoreCommandBuilder(fixture)
    //        .WithTargets("test-pointer-file1.txt.pointer.arius", "test-pointer-file2.txt.pointer.arius")
    //        .WithDownload(true)
    //        .Build();
        
    //    // Act
    //    var result = await handler.Handle(command, CancellationToken.None);
        
    //    // Assert
    //    // Should restore the respective binary files
    //    true.ShouldBe(false, "Test not implemented");
    //}

    //[Fact]
    //public async Task RestoreWithDownload_OneBinaryFile_RestoreRespectiveBinaryFile()
    //{
    //    // Arrange
    //    var command = new RestoreCommandBuilder(fixture)
    //        .WithTargets("test-binary-file.txt")
    //        .WithDownload(true)
    //        .Build();
        
    //    // Act
    //    var result = await handler.Handle(command, CancellationToken.None);
        
    //    // Assert
    //    // Should restore the respective binary file
    //    true.ShouldBe(false, "Test not implemented");
    //}

    //[Fact]
    //public async Task RestoreWithDownload_MultipleBinaryFiles_RestoreRespectiveBinaryFiles()
    //{
    //    // Arrange
    //    var command = new RestoreCommandBuilder(fixture)
    //        .WithTargets("test-binary-file1.txt", "test-binary-file2.txt")
    //        .WithDownload(true)
    //        .Build();
        
    //    // Act
    //    var result = await handler.Handle(command, CancellationToken.None);
        
    //    // Assert
    //    // Should restore the respective binary files
    //    true.ShouldBe(false, "Test not implemented");
    //}

    //[Fact]
    //public async Task RestoreWithDownload_EmptyDirectory_RestoreAllBinaryFiles()
    //{
    //    // Arrange
    //    var command = new RestoreCommandBuilder(fixture)
    //        .WithTargets("empty-directory")
    //        .WithDownload(true)
    //        .Build();
        
    //    // Act
    //    var result = await handler.Handle(command, CancellationToken.None);
        
    //    // Assert
    //    // Should restore all binary files
    //    true.ShouldBe(false, "Test not implemented");
    //}

    //[Fact]
    //public async Task RestoreWithDownload_NonEmptyDirectory_RestoreBinaryFilesForWhichThereArePointerFiles()
    //{
    //    // Arrange
    //    var command = new RestoreCommandBuilder(fixture)
    //        .WithTargets("non-empty-directory")
    //        .WithDownload(true)
    //        .Build();
        
    //    // Act
    //    var result = await handler.Handle(command, CancellationToken.None);
        
    //    // Assert
    //    // Should restore binary files for which there are pointer files
    //    true.ShouldBe(false, "Test not implemented");
    //}

    //[Fact]
    //public async Task RestoreWithDownloadIncludePointers_OnePointerFile_RestoreRespectiveBinaryFile()
    //{
    //    // Arrange
    //    var command = new RestoreCommandBuilder(fixture)
    //        .WithTargets("test-pointer-file.txt.pointer.arius")
    //        .WithDownload(true)
    //        .WithKeepPointers(true)
    //        .Build();
        
    //    // Act
    //    var result = await handler.Handle(command, CancellationToken.None);
        
    //    // Assert
    //    // Should restore the respective binary file
    //    true.ShouldBe(false, "Test not implemented");
    //}

    //[Fact]
    //public async Task RestoreWithDownloadIncludePointers_MultiplePointerFiles_RestoreRespectiveBinaryFiles()
    //{
    //    // Arrange
    //    var command = new RestoreCommandBuilder(fixture)
    //        .WithTargets("test-pointer-file1.txt.pointer.arius", "test-pointer-file2.txt.pointer.arius")
    //        .WithDownload(true)
    //        .WithKeepPointers(true)
    //        .Build();
        
    //    // Act
    //    var result = await handler.Handle(command, CancellationToken.None);
        
    //    // Assert
    //    // Should restore the respective binary files
    //    true.ShouldBe(false, "Test not implemented");
    //}

    //[Fact]
    //public async Task RestoreWithDownloadIncludePointers_OneBinaryFile_RestoreRespectiveBinaryFile()
    //{
    //    // Arrange
    //    var command = new RestoreCommandBuilder(fixture)
    //        .WithTargets("test-binary-file.txt")
    //        .WithDownload(true)
    //        .WithKeepPointers(true)
    //        .Build();
        
    //    // Act
    //    var result = await handler.Handle(command, CancellationToken.None);
        
    //    // Assert
    //    // Should restore the respective binary file
    //    true.ShouldBe(false, "Test not implemented");
    //}

    //[Fact]
    //public async Task RestoreWithDownloadIncludePointers_MultipleBinaryFiles_RestoreRespectiveBinaryFiles()
    //{
    //    // Arrange
    //    var command = new RestoreCommandBuilder(fixture)
    //        .WithTargets("test-binary-file1.txt", "test-binary-file2.txt")
    //        .WithDownload(true)
    //        .WithKeepPointers(true)
    //        .Build();
        
    //    // Act
    //    var result = await handler.Handle(command, CancellationToken.None);
        
    //    // Assert
    //    // Should restore the respective binary files
    //    true.ShouldBe(false, "Test not implemented");
    //}

    //[Fact]
    //public async Task RestoreWithDownloadIncludePointers_EmptyDirectory_RestoreAllBinaryFiles()
    //{
    //    // Arrange
    //    var command = new RestoreCommandBuilder(fixture)
    //        .WithTargets("empty-directory")
    //        .WithDownload(true)
    //        .WithKeepPointers(true)
    //        .Build();
        
    //    // Act
    //    var result = await handler.Handle(command, CancellationToken.None);
        
    //    // Assert
    //    // Should restore all binary files
    //    true.ShouldBe(false, "Test not implemented");
    //}

    //[Fact]
    //public async Task RestoreWithDownloadIncludePointers_NonEmptyDirectory_RestoreBinaryFilesForWhichThereArePointerFiles()
    //{
    //    // Arrange
    //    var command = new RestoreCommandBuilder(fixture)
    //        .WithTargets("non-empty-directory")
    //        .WithDownload(true)
    //        .WithKeepPointers(true)
    //        .Build();
        
    //    // Act
    //    var result = await handler.Handle(command, CancellationToken.None);
        
    //    // Assert
    //    // Should restore binary files for which there are pointer files
    //    true.ShouldBe(false, "Test not implemented");
    //}
}