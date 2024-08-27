using Arius.Core.Domain.Repositories;
using Arius.Core.Domain.Storage;
using Arius.Core.Infrastructure.Repositories;
using Arius.Core.New.UnitTests.Fixtures;
using Azure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using WouterVanRanst.Utils;

namespace Arius.Core.New.UnitTests;

public abstract class SqliteStateDbRepositoryFactoryTestsBase
{
    protected MockAriusFixture Fixture;

    protected void GivenLocalFilesystem()
    {
        Fixture = new MockAriusFixture();
    }

    protected void GivenLocalFilesystemWithVersions(params string[] versionNames)
    {
        GivenLocalFilesystem();
        var repository = Fixture.StorageAccountFactory.GetRepository(Fixture.RepositoryOptions);
        var versions = versionNames.Select(name => new RepositoryVersion { Name = name }).ToArray();
        repository.GetRepositoryVersions().Returns(versions.ToAsyncEnumerable());

        foreach (var versionName in versionNames)
        {
            var version    = new RepositoryVersion { Name = versionName };
            var dbFullName = GetLocalStateDbForRepositoryFullName(Fixture, Fixture.RepositoryOptions, version);

            CreateLocalDatabase(dbFullName);
        }

        void CreateLocalDatabase(string dbFullName)
        {
            var optionsBuilder = new DbContextOptionsBuilder<SqliteStateDbContext>();
            optionsBuilder.UseSqlite($"Data Source={dbFullName}");

            using var context = new SqliteStateDbContext(optionsBuilder.Options);
            context.Database.EnsureCreated();
            context.Database.Migrate();
        }
    }

    protected void GivenAzureRepositoryWithNoVersions()
    {
        var repository = Fixture.StorageAccountFactory.GetRepository(Fixture.RepositoryOptions);
        repository.GetRepositoryVersions().Returns(AsyncEnumerable.Empty<RepositoryVersion>());
    }

    protected void GivenAzureRepositoryWithVersions(params string[] versionNames)
    {
        var repository = Fixture.StorageAccountFactory.GetRepository(Fixture.RepositoryOptions);
        var versions = versionNames.Select(name => new RepositoryVersion { Name = name }).ToArray();
        repository.GetRepositoryVersions().Returns(versions.ToAsyncEnumerable());
    }

    protected void GivenAzureRepositoryWithoutVersion(string versionName)
    {
        var repository = Fixture.StorageAccountFactory.GetRepository(Fixture.RepositoryOptions);
        var blob = Substitute.For<IBlob>();
        var version = new RepositoryVersion { Name = versionName };
        repository.GetRepositoryVersionBlob(version).Returns(blob);
        repository.DownloadAsync(Arg.Any<IBlob>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(Task.FromException(new RequestFailedException(404, "message", "BlobNotFound", null)));
    }

    protected async Task<IStateDbRepository> WhenCreatingStateDb(string versionName = null)
    {
        var factory = Fixture.StateDbRepositoryFactory;
        var repositoryOptions = Fixture.RepositoryOptions;
        var version = versionName != null ? new RepositoryVersion { Name = versionName } : null;
        return await factory.CreateAsync(repositoryOptions, version);
    }

    protected void ThenStateDbVersionShouldBe(IStateDbRepository stateDbRepository, string expectedVersion)
    {
        stateDbRepository.Version.Name.Should().Be(expectedVersion);
    }

    protected void ThenStateDbVersionShouldBeBetween(IStateDbRepository stateDbRepository, DateTime startTime, DateTime endTime)
    {
        DateTime.Parse(stateDbRepository.Version.Name)
            .Should()
            .BeOnOrAfter(startTime).And.BeOnOrBefore(endTime);
    }

    protected void ThenLocalStateDbShouldExist(IStateDbRepository stateDbRepository)
    {
        File.Exists(GetLocalStateDbForRepositoryFullName(Fixture, Fixture.RepositoryOptions, stateDbRepository.Version))
            .Should().BeTrue();
    }

    protected void ThenStateDbShouldBeEmpty(IStateDbRepository stateDbRepository)
    {
        stateDbRepository.GetPointerFileEntries().CountAsync().Result.Should().Be(0);
        stateDbRepository.GetBinaryEntries().CountAsync().Result.Should().Be(0);
    }

    protected void ThenDownloadShouldNotBeCalled()
    {
        var repository = Fixture.StorageAccountFactory.GetRepository(Fixture.RepositoryOptions);
        repository.DidNotReceive().DownloadAsync(Arg.Any<IBlob>(), Arg.Any<string>(), Arg.Any<string>());
    }

    protected void ThenDownloadShouldBeCalled()
    {
        var repository = Fixture.StorageAccountFactory.GetRepository(Fixture.RepositoryOptions);
        repository.Received(1).DownloadAsync(Arg.Any<IBlob>(), Arg.Any<string>(), Arg.Any<string>());
    }

    protected async Task ThenArgumentExceptionShouldBeThrownAsync(Func<Task> act, string expectedMessagePart)
    {
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage($"*{expectedMessagePart}*");
    }

    private string GetLocalStateDbForRepositoryFullName(MockAriusFixture fixture, RepositoryOptions repositoryOptions, RepositoryVersion version)
    {
        return fixture.AriusConfiguration
            .GetLocalStateDbFolderForRepositoryName(repositoryOptions.ContainerName)
            .GetFullName(version.GetFileSystemName());
    }
}

public sealed class SqliteStateDbRepositoryFactoryTests : SqliteStateDbRepositoryFactoryTestsBase
{
    [Fact]
    public async Task CreateAsync_WhenNoVersionsAvailable_ShouldReturnNewFileName()
    {
        // Arrange
        GivenLocalFilesystem();
        GivenAzureRepositoryWithNoVersions();

        var startTime = DateTime.UtcNow.TruncateToSeconds();

        // Act
        var result = await WhenCreatingStateDb();

        // Assert
        var endTime = DateTime.UtcNow.TruncateToSeconds();

        ThenStateDbVersionShouldBeBetween(result, startTime, endTime);
        ThenLocalStateDbShouldExist(result);
        ThenStateDbShouldBeEmpty(result);
        ThenDownloadShouldNotBeCalled();
    }

    [Fact]
    public async Task CreateAsync_WhenNoVersionSpecifiedAndLatestVersionNotCached_ShouldDownloadLatestVersion()
    {
        // Arrange
        GivenLocalFilesystem();
        GivenAzureRepositoryWithVersions("v1.0", "v1.1", "v2.0");

        // Act
        var result = await WhenCreatingStateDb();

        // Assert
        ThenStateDbVersionShouldBe(result, "v2.0");
        ThenLocalStateDbShouldExist(result);
        ThenDownloadShouldBeCalled();
    }

    [Fact]
    public async Task CreateAsync_WhenLatestVersionCached_ShouldReturnCachedRepositoryWithCorrectVersion()
    {
        // Arrange
        GivenLocalFilesystemWithVersions("v1.0", "v1.1", "v2.0");

        // Act
        var result = await WhenCreatingStateDb();

        // Assert
        ThenStateDbVersionShouldBe(result, "v2.0");
        ThenDownloadShouldNotBeCalled();
    }

    [Fact]
    public async Task CreateAsync_WhenSpecificVersionNotCached_ShouldDownloadAndCacheSpecificVersion()
    {
        // Arrange
        GivenLocalFilesystem();
        GivenAzureRepositoryWithVersions("v1.0", "v1.1", "v2.0");

        // Act
        var result = await WhenCreatingStateDb("v1.1");

        // Assert
        result.Should().NotBeNull();
        ThenStateDbVersionShouldBe(result, "v1.1");
        ThenLocalStateDbShouldExist(result);
        ThenDownloadShouldBeCalled();
    }

    [Fact]
    public async Task CreateAsync_WhenSpecificVersionCached_ShouldReturnCachedRepositoryWithCorrectVersion()
    {
        // Arrange
        GivenLocalFilesystemWithVersions("v1.0", "v1.1", "v2.0");

        // Act
        var result = await WhenCreatingStateDb("v1.1");

        // Assert
        result.Should().NotBeNull();
        ThenStateDbVersionShouldBe(result, "v1.1");
        ThenDownloadShouldNotBeCalled();
    }

    [Fact]
    public async Task CreateAsync_WhenSpecificVersionDoesNotExist_ShouldThrowArgumentException()
    {
        // Arrange
        GivenLocalFilesystemWithVersions("v1.0", "v2.0");
        GivenAzureRepositoryWithoutVersion("v3.0");

        // Act
        Func<Task> act = async () => await WhenCreatingStateDb("v3.0");

        // Assert
        await ThenArgumentExceptionShouldBeThrownAsync(act, "The requested version was not found*");
    }

    [Fact]
    public async Task CreateAsync_WhenSpecificVersionExistsLocally_ShouldNotDownloadAndUseLocalVersion()
    {
        // Arrange
        GivenLocalFilesystemWithVersions("v1.0", "v1.1", "v2.0");

        // Act
        var result = await WhenCreatingStateDb("v1.1");

        // Assert
        result.Should().NotBeNull();
        ThenStateDbVersionShouldBe(result, "v1.1");
        ThenLocalStateDbShouldExist(result);
        ThenDownloadShouldNotBeCalled();
    }
}