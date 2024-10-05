using Arius.Core.Domain.Repositories;
using Arius.Core.Domain.Storage;
using Arius.Core.Domain.Storage.FileSystem;
using Arius.Core.Infrastructure.Repositories;
using Arius.Core.Infrastructure.Storage.Azure;
using Arius.Core.Infrastructure.Storage.LocalFileSystem;
using Arius.Core.New.UnitTests.Fixtures;
using Azure;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ClearExtensions;
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed

namespace Arius.Core.New.UnitTests;

public class SqliteRemoteStateRepositoryTests : TestBase
{
    protected override AriusFixture GetFixture()
    {
        return new FixtureBuilder()
            .WithMockedStorageAccountFactory()
            .WithFakeCryptoService()
            .WithUniqueContainerName()
            .Build();
    }

    protected override void ConfigureOnceForFixture()
    {
    }

    private readonly IAzureContainerFolder  containerFolder;
    private readonly IRemoteStateRepository repository;
    private readonly DirectoryInfo          localStateDatabaseCacheDirectory;

    public SqliteRemoteStateRepositoryTests()
    {
        localStateDatabaseCacheDirectory = Fixture.AriusConfiguration.GetLocalStateDatabaseCacheDirectoryForContainerName(Fixture.RemoteRepositoryOptions.ContainerName);

        var loggerFactory = NullLoggerFactory.Instance;
        var logger        = NullLogger<SqliteRemoteStateRepository>.Instance;

        containerFolder = Substitute.For<IAzureContainerFolder>();
        repository      = new SqliteRemoteStateRepository(containerFolder, loggerFactory, logger);

        // it returns an IAzureBlob with the requested name
        containerFolder.GetBlob(Arg.Any<string>())
            .Returns(i => Substitute.For<IAzureBlob>()
                .With(b => b.Name.Returns(i.Arg<string>())));
    }

    ///// <summary>
    ///// Get an existing repository version. 
    ///// If `version` is null, it will get the latest version. If there is no version, it will return null.
    ///// If `version` is specified but does not exist, it will throw an exception.
    ///// </summary>
    //public Task<ILocalStateRepository?> CreateNewLocalStateRepositoryAsync(DirectoryInfo localStateDatabaseCacheDirectory, StateVersion? version = null);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GetLocalStateRepositoryAsync_WhenVersionIsNullAndExists_ShouldReturnLatestVersion(bool isLocallyCached)
    {
        // Arrange
        var latestVersion = StateVersion.FromName("v2.0");
        containerFolder.GetBlobs().Returns(x => GetMockBlobs(["v1.0", "v1.1", "v2.0"]));
        containerFolder.DownloadAsync(Arg.Any<IAzureBlob>(), Arg.Any<IFile>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                CreateLocalDatabaseWithEntry(localStateDatabaseCacheDirectory, latestVersion, ["test"]);
                return Task.CompletedTask;
            });

        if (isLocallyCached)
        {
            var sdbf = StateDatabaseFile.FromRepositoryVersion(localStateDatabaseCacheDirectory, latestVersion);
            CreateLocalDatabase(sdbf);
        }

        // Act
        var localStateRepository = await repository.GetLocalStateRepositoryAsync(localStateDatabaseCacheDirectory, version: null);

        // Assert
        if (isLocallyCached)
            containerFolder.DidNotReceiveWithAnyArgs().DownloadAsync(default, default);
        else
            containerFolder.Received(1).DownloadAsync(Arg.Is<IAzureBlob>(b => b.Name == latestVersion.Name), Arg.Any<IFile>());
        
        localStateRepository.Should().NotBeNull();
        localStateRepository.Version.Should().Be(latestVersion);
        localStateRepository.StateDatabaseFile.Exists.Should().BeTrue();
    }

    [Fact]
    public async Task GetLocalStateRepositoryAsync_WhenVersionIsNullAndNoVersionsExist_ShouldReturnNull()
    {
        // Arrange
        containerFolder.GetBlobs().Returns(x => AsyncEnumerable.Empty<IAzureBlob>());

        // Act
        var localStateRepository = await repository.GetLocalStateRepositoryAsync(localStateDatabaseCacheDirectory, version: null);

        // Assert
        containerFolder.DidNotReceiveWithAnyArgs().DownloadAsync(default, default);
        localStateRepository.Should().BeNull();
    }

    [Fact]
    public async Task GetLocalStateRepositoryAsync_WhenSpecifiedVersionDoesNotExist_ShouldThrowException()
    {
        // Arrange
        var requestedVersion = StateVersion.FromName("non-existent");

        containerFolder.ClearSubstitute();
        containerFolder.GetBlob(requestedVersion.Name)
            .Returns(_ => throw new RequestFailedException(0, "", "BlobNotFound", null));

        // Act
        Func<Task> act = async () => await repository.GetLocalStateRepositoryAsync(localStateDatabaseCacheDirectory, requestedVersion);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("The requested version was not found*");
    }


    ///// <summary>
    ///// Create a new repository version based on an existing one.
    ///// If `basedOn` is null, it will be based on the latest version.
    ///// If `basedOn` is specified, but does not exist, it will throw an exception.
    ///// </summary>
    ////public Task<ILocalStateRepository> CreateNewLocalStateRepositoryAsync(DirectoryInfo localStateDatabaseCacheDirectory, StateVersion version, StateVersion? basedOn = null);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CreateNewLocalStateRepositoryAsync_WhenBasedOnIsNull_BasedOnLatestVersion(bool isLocallyCached)
    {
        // Arrange
        var latestVersion = StateVersion.FromName("v2.0");

        if (isLocallyCached)
        {
            CreateLocalDatabaseWithEntry(localStateDatabaseCacheDirectory, latestVersion,["test"]);
        }

        containerFolder.GetBlobs().Returns(x => GetMockBlobs(["v1.0", "v1.1", "v2.0"]));
        containerFolder.DownloadAsync(Arg.Is<IAzureBlob>(b => b.Name == latestVersion.Name), Arg.Any<IFile>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                CreateLocalDatabaseWithEntry(localStateDatabaseCacheDirectory, latestVersion, ["test"]);
                return Task.CompletedTask;
            });

        var newVersion = StateVersion.FromName("NewVersion");

        // Act
        var localStateRepository = await repository.CreateNewLocalStateRepositoryAsync(localStateDatabaseCacheDirectory, newVersion, basedOn: null);

        // Assert
        if (isLocallyCached)
            containerFolder.DidNotReceiveWithAnyArgs().DownloadAsync(default, default);
        else
            containerFolder.Received(1).DownloadAsync(Arg.Is<IAzureBlob>(b => b.Name == latestVersion.Name), Arg.Any<IFile>());

        localStateRepository.Version.Should().Be(newVersion);
        localStateRepository.StateDatabaseFile.Exists.Should().BeTrue();
        LocalDatabaseHasEntry(localStateRepository, "test");
    }

    [Fact]
    public async Task CreateNewLocalStateRepositoryAsync_WhenBasedOnIsNullButNoVersionsExist_NewLocalDatabaseInitializedNotDownloaded()
    {
        // Arrange
        var newVersion = StateVersion.FromName("NewVersion");
        containerFolder.GetBlobs().Returns(_ => AsyncEnumerable.Empty<IAzureBlob>());

        // Act
        var localStateRepository = await repository.CreateNewLocalStateRepositoryAsync(localStateDatabaseCacheDirectory, newVersion, basedOn: null);

        // Assert
        localStateRepository.Version.Should().Be(newVersion);
        localStateRepository.StateDatabaseFile.Exists.Should().BeTrue();
        LocalStateRepositoryShouldBeEmpty(localStateRepository);


    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CreateNewLocalStateRepositoryAsync_WhenBasedOnIsNotNullAndExists_BasedOnThatVersion(bool isLocallyCached)
    {
        // Arrange
        var basedOnVersion = StateVersion.FromName("v1.1");

        if (isLocallyCached)
        {
            CreateLocalDatabaseWithEntry(localStateDatabaseCacheDirectory, basedOnVersion, ["test"]);
        }
        else
        {
            containerFolder.DownloadAsync(Arg.Is<IAzureBlob>(b => b.Name.StartsWith("v1.1")), Arg.Any<IFile>(), Arg.Any<CancellationToken>())
                .Returns(i =>
                {
                    CreateLocalDatabaseWithEntry(localStateDatabaseCacheDirectory, basedOnVersion, ["test"]);
                    return Task.CompletedTask;
                });
        }

        containerFolder.GetBlobs().Returns(x => GetMockBlobs(["v1.0", "v1.1", "v2.0"]));
        
        var newVersion = StateVersion.FromName("NewVersion");

        // Act
        var localStateRepository = await repository.CreateNewLocalStateRepositoryAsync(localStateDatabaseCacheDirectory, newVersion, basedOn: basedOnVersion);

        // Assert
        if (isLocallyCached)
            containerFolder.DidNotReceiveWithAnyArgs().DownloadAsync(default, default);
        else
            containerFolder.Received(1).DownloadAsync(Arg.Is<IAzureBlob>(b => b.Name == basedOnVersion.Name), Arg.Any<IFile>());

        localStateRepository.Version.Should().Be(newVersion);
        localStateRepository.StateDatabaseFile.Exists.Should().BeTrue();
        LocalDatabaseHasEntry(localStateRepository, "test");
    }

    [Fact]
    public async Task CreateNewLocalStateRepositoryAsync_WhenBasedOnIsNotNullAndDoesNotExist_ShouldThrowException()
    {
        // Arrange
        var requestedVersion = StateVersion.FromName("non-existent");

        containerFolder.ClearSubstitute();
        containerFolder.GetBlob(requestedVersion.Name)
            .Returns(_ => throw new RequestFailedException(0, "", "BlobNotFound", null));

        // Act
        Func<Task> act = async () => await repository.CreateNewLocalStateRepositoryAsync(localStateDatabaseCacheDirectory, StateVersion.FromName("NewVersion"), basedOn: requestedVersion);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("The requested version was not found*");
    }


    private static IAsyncEnumerable<IAzureBlob> GetMockBlobs(string[] versionNames)
    {
        return versionNames
            .Select(v => Substitute.For<IAzureBlob>()
                .With(b => b.Name.Returns(v)))
            .ToAsyncEnumerable();
    }

    private static void LocalStateRepositoryShouldBeEmpty(ILocalStateRepository localStateRepository)
    {
        localStateRepository.CountPointerFileEntries().Should().Be(0);
        localStateRepository.CountBinaryProperties().Should().Be(0);
    }

    //   TODO tests for SaveChangesAsync?
}