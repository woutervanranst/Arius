using Arius.Core.Features.Archive;
using Arius.Core.Shared.StateRepositories;
using Arius.Core.Shared.Storage;
using Arius.Core.Tests.Helpers.Builders;
using Arius.Core.Tests.Helpers.FakeLogger;
using Arius.Core.Tests.Helpers.Fixtures;
using NSubstitute;
using Shouldly;
using Zio;

namespace Arius.Core.Tests.Features.Archive;

public class ArchiveCommandHandlerContextCreateAsyncTests
{
    private readonly FakeLoggerFactory     loggerFactory;
    private readonly IArchiveStorage       mockArchiveStorage;
    private readonly StateCache            stateCache;
    private readonly ArchiveCommand        testCommand;
    private readonly FixtureWithFileSystem fixture;

    public ArchiveCommandHandlerContextCreateAsyncTests()
    {
        fixture            = new FixtureWithFileSystem();
        loggerFactory      = new FakeLoggerFactory();
        mockArchiveStorage = Substitute.For<IArchiveStorage>();

        testCommand = new ArchiveCommandBuilder(fixture).Build();

        stateCache = new StateCache(testCommand.AccountName, testCommand.ContainerName);
    }

    [Fact]
    public async Task CreateAsync_WhenNoRemoteStateExists_ShouldCreateNewStateFile()
    {
        // Arrange
        mockArchiveStorage.CreateContainerIfNotExistsAsync().Returns(true);
        mockArchiveStorage.GetStates(Arg.Any<CancellationToken>()).Returns(AsyncEnumerable.Empty<string>());

        // Act
        var context = await new HandlerContextBuilder(testCommand, loggerFactory)
            .WithArchiveStorage(mockArchiveStorage)
            .BuildAsync();

        // Assert
        context.ShouldNotBeNull();
        context.Request.ShouldBe(testCommand);
        context.ArchiveStorage.ShouldBe(mockArchiveStorage);
        context.StateRepository.ShouldNotBeNull();
        context.Hasher.ShouldNotBeNull();
        context.FileSystem.ShouldNotBeNull();

        // Verify a new state file was created (with current timestamp format)
        var stateFiles = stateCache.GetStateFileEntries().ToArray();
        stateFiles.Length.ShouldBe(1);
        stateFiles[0].Name.ShouldMatch(@"\d{4}-\d{2}-\d{2}T\d{2}-\d{2}-\d{2}\.db");

        // Verify no download was attempted since no remote state exists
        await mockArchiveStorage.DidNotReceive().DownloadStateAsync(Arg.Any<string>(), Arg.Any<FileEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_WhenRemoteStateExistsButNotLocally_ShouldDownloadAndCreateNewVersion()
    {
        // Arrange
        const string existingStateName = "2024-01-01T12-00-00";
        mockArchiveStorage.CreateContainerIfNotExistsAsync().Returns(true);
        mockArchiveStorage.GetStates(Arg.Any<CancellationToken>()).Returns(new[] { existingStateName }.ToAsyncEnumerable());

        // Mock the download behavior to create a valid state file when download is called
        mockArchiveStorage.DownloadStateAsync(Arg.Any<string>(), Arg.Any<FileEntry>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask)
            .AndDoes(callInfo =>
            {
                var targetFile = callInfo.ArgAt<FileEntry>(1);
                new StateRepositoryBuilder().Build(stateCache, existingStateName);
            });

        // Act
        var context = await new HandlerContextBuilder(testCommand, loggerFactory)
            .WithArchiveStorage(mockArchiveStorage)
            .BuildAsync();

        // Assert
        context.ShouldNotBeNull();
        context.Request.ShouldBe(testCommand);
        context.ArchiveStorage.ShouldBe(mockArchiveStorage);

        // Verify the remote state was downloaded
        await mockArchiveStorage.Received(1).DownloadStateAsync(
            existingStateName, 
            Arg.Is<FileEntry>(fi => fi.Name == $"{existingStateName}.db"), 
            Arg.Any<CancellationToken>());

        // Verify a new state file was created (with current timestamp format)
        var stateFiles = stateCache.GetStateFileEntries().ToArray();
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
        new StateRepositoryBuilder().Build(stateCache, existingStateName);

        // Act
        var context = await new HandlerContextBuilder(testCommand, loggerFactory)
            .WithArchiveStorage(mockArchiveStorage)
            .BuildAsync();

        // Assert
        context.ShouldNotBeNull();
        context.Request.ShouldBe(testCommand);
        context.ArchiveStorage.ShouldBe(mockArchiveStorage);

        // Verify no download was attempted since the file already exists locally
        await mockArchiveStorage.DidNotReceive().DownloadStateAsync(Arg.Any<string>(), Arg.Any<FileEntry>(), Arg.Any<CancellationToken>());

        // Verify a new state file was created (with current timestamp format)
        var stateFiles = stateCache.GetStateFileEntries().ToArray();
        stateFiles.Length.ShouldBeGreaterThan(1);
        
        // Should have the existing state file and the new version
        var existingFile = stateFiles.FirstOrDefault(f => f.Name == $"{existingStateName}.db");
        existingFile.ShouldNotBeNull("the existing state file should still exist");
        
        var newStateFile = stateFiles.FirstOrDefault(f => f.Name != $"{existingStateName}.db");
        newStateFile.ShouldNotBeNull("a new state file should be created");
        newStateFile.Name.ShouldMatch(@"\d{4}-\d{2}-\d{2}T\d{2}-\d{2}-\d{2}\.db");
    }
}