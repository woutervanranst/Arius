using Arius.Core.Features.Archive;
using Arius.Core.Shared.Storage;
using Arius.Core.Tests.Builders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
using NSubstitute;
using Shouldly;

namespace Arius.Core.Tests.Commands;

public class ArchiveCommandHandlerContextCreateAsyncTests : IDisposable
{
    private readonly FakeLogger<ArchiveCommandHandler.HandlerContext> logger;
    private readonly IArchiveStorage                                  mockArchiveStorage;
    private readonly DirectoryInfo                                    tempStateDirectory;
    private readonly ArchiveCommand                                   testCommand;
    private readonly Fixture                                          fixture;

    public ArchiveCommandHandlerContextCreateAsyncTests()
    {
        fixture = new Fixture();
        logger = new FakeLogger<ArchiveCommandHandler.HandlerContext>();
        mockArchiveStorage = Substitute.For<IArchiveStorage>();
        
        tempStateDirectory = new DirectoryInfo(Path.Combine(Path.GetTempPath(), $"arius-test-{Guid.NewGuid()}"));
        tempStateDirectory.Create();

        testCommand = new ArchiveCommandBuilder(fixture).Build();
    }

    [Fact]
    public async Task CreateAsync_WhenNoRemoteStateExists_ShouldCreateNewStateFile()
    {
        // Arrange
        mockArchiveStorage.CreateContainerIfNotExistsAsync().Returns(true);
        mockArchiveStorage.GetStates(Arg.Any<CancellationToken>()).Returns(AsyncEnumerable.Empty<string>());

        // Act
        var context = await ArchiveCommandHandler.HandlerContext.CreateAsync(
            testCommand, 
            NullLoggerFactory.Instance, 
            mockArchiveStorage, 
            tempStateDirectory);

        // Assert
        context.ShouldNotBeNull();
        context.Request.ShouldBe(testCommand);
        context.ArchiveStorage.ShouldBe(mockArchiveStorage);
        context.StateRepository.ShouldNotBeNull();
        context.Hasher.ShouldNotBeNull();
        context.FileSystem.ShouldNotBeNull();

        // Verify a new state file was created (with current timestamp format)
        var stateFiles = tempStateDirectory.GetFiles("*.db");
        stateFiles.Length.ShouldBe(1);
        stateFiles[0].Name.ShouldMatch(@"\d{4}-\d{2}-\d{2}T\d{2}-\d{2}-\d{2}\.db");

        // Verify no download was attempted since no remote state exists
        await mockArchiveStorage.DidNotReceive().DownloadStateAsync(Arg.Any<string>(), Arg.Any<FileInfo>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_WhenRemoteStateExistsButNotLocally_ShouldDownloadAndCreateNewVersion()
    {
        // Arrange
        const string existingStateName = "2024-01-01T12-00-00";
        mockArchiveStorage.CreateContainerIfNotExistsAsync().Returns(true);
        mockArchiveStorage.GetStates(Arg.Any<CancellationToken>()).Returns(new[] { existingStateName }.ToAsyncEnumerable());

        // Mock the download behavior to create a valid state file when download is called
        mockArchiveStorage.DownloadStateAsync(Arg.Any<string>(), Arg.Any<FileInfo>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask)
            .AndDoes(callInfo =>
            {
                var targetFile = callInfo.ArgAt<FileInfo>(1);
                new StateRepositoryBuilder().Build(tempStateDirectory.FullName, existingStateName);
            });

        // Act
        var context = await ArchiveCommandHandler.HandlerContext.CreateAsync(
            testCommand, 
            NullLoggerFactory.Instance, 
            mockArchiveStorage, 
            tempStateDirectory);

        // Assert
        context.ShouldNotBeNull();
        context.Request.ShouldBe(testCommand);
        context.ArchiveStorage.ShouldBe(mockArchiveStorage);

        // Verify the remote state was downloaded
        await mockArchiveStorage.Received(1).DownloadStateAsync(
            existingStateName, 
            Arg.Is<FileInfo>(fi => fi.Name == $"{existingStateName}.db"), 
            Arg.Any<CancellationToken>());

        // Verify a new state file was created (with current timestamp format)
        var stateFiles = tempStateDirectory.GetFiles("*.db");
        stateFiles.Length.ShouldBeGreaterThan(0);
        
        // Should have the downloaded state file and the new version
        var downloadedStateFile = stateFiles.FirstOrDefault(f => f.Name == $"{existingStateName}.db");
        downloadedStateFile.ShouldNotBeNull("the downloaded state file should exist");
        
        var newStateFile = stateFiles.FirstOrDefault(f => f.Name != $"{existingStateName}.db");
        newStateFile.ShouldNotBeNull("a new state file should be created");
        newStateFile.Name.ShouldMatch(@"\d{4}-\d{2}-\d{2}T\d{2}-\d{2}-\d{2}\.db");
    }

    [Fact]
    public async Task CreateAsync_WhenRemoteStateExistsAndIsPresentLocally_ShouldNotDownload()
    {
        // Arrange
        const string existingStateName = "2024-01-01T12-00-00";
        
        mockArchiveStorage.CreateContainerIfNotExistsAsync().Returns(true);
        mockArchiveStorage.GetStates(Arg.Any<CancellationToken>()).Returns(new[] { existingStateName }.ToAsyncEnumerable());

        // Pre-create a valid state file locally to simulate it already being cached
        new StateRepositoryBuilder().Build(tempStateDirectory.FullName, existingStateName);

        // Act
        var context = await ArchiveCommandHandler.HandlerContext.CreateAsync(
            testCommand, 
            NullLoggerFactory.Instance, 
            mockArchiveStorage, 
            tempStateDirectory);

        // Assert
        context.ShouldNotBeNull();
        context.Request.ShouldBe(testCommand);
        context.ArchiveStorage.ShouldBe(mockArchiveStorage);

        // Verify no download was attempted since the file already exists locally
        await mockArchiveStorage.DidNotReceive().DownloadStateAsync(Arg.Any<string>(), Arg.Any<FileInfo>(), Arg.Any<CancellationToken>());

        // Verify a new state file was created (with current timestamp format)
        var stateFiles = tempStateDirectory.GetFiles("*.db");
        stateFiles.Length.ShouldBeGreaterThan(1);
        
        // Should have the existing state file and the new version
        var existingFile = stateFiles.FirstOrDefault(f => f.Name == $"{existingStateName}.db");
        existingFile.ShouldNotBeNull("the existing state file should still exist");
        
        var newStateFile = stateFiles.FirstOrDefault(f => f.Name != $"{existingStateName}.db");
        newStateFile.ShouldNotBeNull("a new state file should be created");
        newStateFile.Name.ShouldMatch(@"\d{4}-\d{2}-\d{2}T\d{2}-\d{2}-\d{2}\.db");
    }

    public void Dispose()
    {
        fixture?.Dispose();
        //if (tempStateDirectory.Exists)
        //{
        //    tempStateDirectory.Delete(recursive: true);
        //}
    }
}