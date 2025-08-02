// Arius.Core.Tests/Commands/ArchiveCommandHandler.State.Tests.cs

using Arius.Core.Commands;
using Arius.Core.Repositories;
using Arius.Core.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Zio;
using Zio.FileSystems;
using Arius.Core.Tests.Extensions;

namespace Arius.Core.Tests.Commands;

public class ArchiveCommandHandler_StateTests
{
    private readonly IBlobStorage mockBlobStorage;
    private readonly MemoryFileSystem   memoryFileSystem;
    private readonly DirectoryEntry     stateCacheRoot;
    private readonly ArchiveCommand     testCommand;
    private readonly FilePairFileSystem fileSystem;

    public ArchiveCommandHandler_StateTests()
    {
        mockBlobStorage = Substitute.For<IBlobStorage>();
        memoryFileSystem = new MemoryFileSystem();
        stateCacheRoot = new DirectoryEntry(memoryFileSystem, UPath.Root / "statecache");
        fileSystem = new FilePairFileSystem(memoryFileSystem);

        // Create a dummy command object for the test
        testCommand = new ArchiveCommand
        {
            AccountName = "testaccount",
            AccountKey = "testkey",
            ContainerName = "testcontainer",
            Passphrase = "testpassphrase",
            LocalRoot = new DirectoryInfo("C:\\test"),
            RemoveLocal = false, // Add this
            Tier = Arius.Core.Models.StorageTier.Archive // Add this
        };
    }

    [Fact]
    public async Task CreateContext_WhenNoRemoteStateExists_CreatesNewStateFile()
    {
        // ARRANGE
        // 1. Mock BlobStorage to return no states
        mockBlobStorage.GetStates().Returns(AsyncEnumerable.Empty<string>());

        // 2. The in-memory statecache directory is empty
        stateCacheRoot.Create();

        // ACT
        var context = await ArchiveCommandHandler.HandlerContext.CreateAsync(
            testCommand,
            NullLoggerFactory.Instance,
            mockBlobStorage,
            stateCacheRoot.ToDirectoryInfo(),
            fileSystem);

        // ASSERT
        // 1. It checked for remote states
        mockBlobStorage.Received(1).GetStates();

        // 2. It did NOT try to download anything
        mockBlobStorage.DidNotReceive().DownloadStateAsync(Arg.Any<string>(), Arg.Any<FileInfo>());

        // 3. A new state file was created in the in-memory file system
        var stateFiles = memoryFileSystem.EnumerateFiles(stateCacheRoot.Path, "*.sqlite");
        stateFiles.Should().HaveCount(1);

        // 4. The context's repository points to this new file
        context.StateRepo.StateDatabaseFile.FullName.Should().Be(stateFiles.Single().FullName);
        
        // 5. The new database should be empty
        context.StateRepo.GetPointerFileEntries().Any().Should().BeFalse();
    }

    [Fact]
    public async Task CreateContext_WhenRemoteStateExistsButNotLocal_DownloadsAndCopiesState()
    {
        // ARRANGE
        var latestStateName = "2023-10-27T12-00-00";
        var latestStateFileOnDisk = stateCacheRoot.Path / $"{latestStateName}.sqlite";

        // 1. Mock BlobStorage to return one state
        mockBlobStorage.GetStates().Returns(new[] { latestStateName }.ToAsyncEnumerable());

        // 2. Mock the download to create a dummy file in our in-memory file system
        mockBlobStorage.DownloadStateAsync(latestStateName, Arg.Any<FileInfo>())
            .Returns(Task.CompletedTask)
            .AndDoes(_ => memoryFileSystem.WriteAllText(latestStateFileOnDisk, "content of remote state"));

        // 3. The in-memory statecache directory is empty
        stateCacheRoot.Create();

        // ACT
        var context = await ArchiveCommandHandler.HandlerContext.CreateAsync(
            testCommand,
            NullLoggerFactory.Instance,
            mockBlobStorage,
            stateCacheRoot.ToDirectoryInfo(),
            fileSystem);

        // ASSERT
        // 1. The download method was called exactly once
        mockBlobStorage.Received(1).DownloadStateAsync(latestStateName, Arg.Is<FileInfo>(fi => fi.Name.Contains(latestStateName)));

        // 2. Two state files now exist in the cache: the downloaded one and the new version
        var stateFiles = memoryFileSystem.EnumerateFiles(stateCacheRoot.Path, "*.sqlite").ToList();
        stateFiles.Should().HaveCount(2);

        // 3. The downloaded file exists and has the correct content
        memoryFileSystem.FileExists(latestStateFileOnDisk).Should().BeTrue();
        memoryFileSystem.ReadAllText(latestStateFileOnDisk).Should().Be("content of remote state");

        // 4. The new state file is a copy of the downloaded one
        var newStateFile = stateFiles.Single(f => f.FullName != latestStateFileOnDisk.FullName);
        memoryFileSystem.ReadAllText(newStateFile.FullName).Should().Be("content of remote state");
        
        // 5. The context's repository points to the new file
        context.StateRepo.StateDatabaseFile.FullName.Should().Be(newStateFile.FullName);
    }

    [Fact]
    public async Task CreateContext_WhenRemoteStateExistsAndIsLocal_DoesNotDownload()
    {
        // ARRANGE
        var latestStateName = "2023-10-27T12-00-00";
        var latestStateFileOnDisk = stateCacheRoot.Path / $"{latestStateName}.sqlite";

        // 1. Mock BlobStorage to return one state
        mockBlobStorage.GetStates().Returns(new[] { latestStateName }.ToAsyncEnumerable());

        // 2. Pre-populate the in-memory file system with the "already downloaded" state file
        stateCacheRoot.Create();
        memoryFileSystem.WriteAllText(latestStateFileOnDisk, "content of LOCAL state");

        // ACT
        var context = await ArchiveCommandHandler.HandlerContext.CreateAsync(
            testCommand,
            NullLoggerFactory.Instance,
            mockBlobStorage,
            stateCacheRoot.ToDirectoryInfo(),
            fileSystem);

        // ASSERT
        // 1. The download method was NEVER called, because the file was found locally
        mockBlobStorage.DidNotReceive().DownloadStateAsync(Arg.Any<string>(), Arg.Any<FileInfo>());

        // 2. Two state files still exist: the original local one and the new version
        var stateFiles = memoryFileSystem.EnumerateFiles(stateCacheRoot.Path, "*.sqlite").ToList();
        stateFiles.Should().HaveCount(2);

        // 3. The new state file is a copy of the LOCAL one
        var newStateFile = stateFiles.Single(f => f.FullName != latestStateFileOnDisk.FullName);
        memoryFileSystem.ReadAllText(newStateFile.FullName).Should().Be("content of LOCAL state");

        // 4. The context's repository points to the new file
        context.StateRepo.StateDatabaseFile.FullName.Should().Be(newStateFile.FullName);
    }
}