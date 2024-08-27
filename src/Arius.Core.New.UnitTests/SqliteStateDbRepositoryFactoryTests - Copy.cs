using Arius.Core.Domain;
using Arius.Core.Domain.Repositories;
using Arius.Core.Domain.Storage;
using Arius.Core.Infrastructure.Repositories;
using Arius.Core.New.UnitTests.Fixtures;
using Azure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using System.IO.Abstractions.TestingHelpers;

namespace Arius.Core.New.UnitTests;

public sealed class SqliteStateDbRepositoryFactoryTests2 : IClassFixture<RequestHandlerFixture>
{
    private readonly RequestHandlerFixture          fixture;
    private          MockFileSystem                 mockFileSystem;
    private          IStorageAccountFactory         mockStorageAccountFactory;
    private          SqliteStateDbRepositoryFactory repositoryFactory;
    private          IStateDbRepository             stateDbRepository;
    private          RepositoryOptions              repositoryOptions;

    public SqliteStateDbRepositoryFactoryTests2(RequestHandlerFixture fixture)
    {
        this.fixture = fixture;
    }

    [Theory]
    [InlineData(ServiceConfiguration.Mocked)]
    [InlineData(ServiceConfiguration.Real)]
    public async Task CreateAsync_WhenNoVersionsAvailable_ShouldReturnNewFileName(ServiceConfiguration configuration)
    {
        // Arrange
        GivenAzureRepositoryWithNoVersions(configuration);
        GivenLocalFilesystem(configuration);
        WhenRepositoryIsCreated();

        // Act
        var result = await repositoryFactory.CreateAsync(repositoryOptions);

        // Assert
        ThenNewFileShouldBeCreatedWithCurrentTimestamp(result);
        ThenRepositoryVersionShouldBeTheTimestamp(result);
    }

    [Theory]
    [InlineData(ServiceConfiguration.Mocked)]
    [InlineData(ServiceConfiguration.Real)]
    public async Task CreateAsync_WhenNoVersionSpecifiedAndLatestVersionNotCached_ShouldDownloadLatestVersion(ServiceConfiguration configuration)
    {
        // Arrange
        GivenAzureRepositoryWithVersions(configuration, "v1.0", "v1.1", "v2.0");
        GivenLocalFilesystem(configuration);
        WhenRepositoryIsCreated();

        // Act
        var result = await repositoryFactory.CreateAsync(repositoryOptions);

        // Assert
        ThenLatestVersionShouldBeDownloaded(result, "v2.0");
        ThenFileShouldBeCachedLocally(result);
    }

    [Theory]
    [InlineData(ServiceConfiguration.Mocked)]
    [InlineData(ServiceConfiguration.Real)]
    public async Task CreateAsync_WhenLatestVersionCached_ShouldReturnCachedRepositoryWithCorrectVersion(ServiceConfiguration configuration)
    {
        // Arrange
        GivenAzureRepositoryWithVersions(configuration, "v1.0", "v1.1", "v2.0");
        GivenLocalFilesystemWithCachedFile(configuration, "v2.0");
        WhenRepositoryIsCreated();

        // Act
        var result = await repositoryFactory.CreateAsync(repositoryOptions);

        // Assert
        ThenRepositoryVersionShouldBe(result, "v2.0");
        ThenDownloadAsyncShouldNotBeCalled(configuration);
    }

    [Theory]
    [InlineData(ServiceConfiguration.Mocked)]
    [InlineData(ServiceConfiguration.Real)]
    public async Task CreateAsync_WhenSpecificVersionNotCached_ShouldDownloadAndCacheSpecificVersion(ServiceConfiguration configuration)
    {
        // Arrange
        GivenAzureRepositoryWithVersions(configuration, "v1.0", "v1.1", "v2.0");
        GivenLocalFilesystem(configuration);
        WhenRepositoryIsCreated();

        // Act
        var requestedVersion = new RepositoryVersion { Name = "v1.1" };
        var result           = await repositoryFactory.CreateAsync(repositoryOptions, requestedVersion);

        // Assert
        ThenRepositoryVersionShouldBe(result, "v1.1");
        ThenFileShouldBeCachedLocally(result);
        ThenDownloadAsyncShouldBeCalled(configuration);
    }

    [Theory]
    [InlineData(ServiceConfiguration.Mocked)]
    [InlineData(ServiceConfiguration.Real)]
    public async Task CreateAsync_WhenSpecificVersionCached_ShouldReturnCachedRepositoryWithCorrectVersion(ServiceConfiguration configuration)
    {
        // Arrange
        GivenAzureRepositoryWithVersions(configuration, "v1.0", "v1.1", "v2.0");
        GivenLocalFilesystemWithCachedFile(configuration, "v1.1");
        WhenRepositoryIsCreated();

        // Act
        var requestedVersion = new RepositoryVersion { Name = "v1.1" };
        var result           = await repositoryFactory.CreateAsync(repositoryOptions, requestedVersion);

        // Assert
        ThenRepositoryVersionShouldBe(result, "v1.1");
        ThenDownloadAsyncShouldNotBeCalled(configuration);
    }

    [Theory]
    [InlineData(ServiceConfiguration.Mocked)]
    [InlineData(ServiceConfiguration.Real)]
    public async Task CreateAsync_WhenSpecificVersionDoesNotExist_ShouldThrowArgumentException(ServiceConfiguration configuration)
    {
        // Arrange
        GivenAzureRepositoryWithVersionButNotExist(configuration, "v3.0");
        GivenLocalFilesystem(configuration);
        WhenRepositoryIsCreated();

        // Act
        Func<Task> act = async () => await repositoryFactory.CreateAsync(repositoryOptions, new RepositoryVersion { Name = "v3.0" });

        // Assert
        await act.Should().ThrowAsync<ArgumentException>().WithMessage("The requested version was not found*");
    }

    private void GivenAzureRepositoryWithNoVersions(ServiceConfiguration configuration)
    {
        mockStorageAccountFactory = Substitute.For<IStorageAccountFactory>();
        var repository = Substitute.For<IRepository>();
        repository.GetRepositoryVersions().Returns(AsyncEnumerable.Empty<RepositoryVersion>());
        mockStorageAccountFactory.GetRepository(Arg.Any<RepositoryOptions>()).Returns(repository);

        SetupFactory(configuration);
    }

    private void GivenAzureRepositoryWithVersions(ServiceConfiguration configuration, params string[] versions)
    {
        mockStorageAccountFactory = Substitute.For<IStorageAccountFactory>();
        var repository = Substitute.For<IRepository>();
        repository.GetRepositoryVersions().Returns(versions.Select(v => new RepositoryVersion { Name = v }).ToAsyncEnumerable());
        mockStorageAccountFactory.GetRepository(Arg.Any<RepositoryOptions>()).Returns(repository);

        SetupFactory(configuration);
    }

    private void GivenAzureRepositoryWithVersionButNotExist(ServiceConfiguration configuration, string versionName)
    {
        mockStorageAccountFactory = Substitute.For<IStorageAccountFactory>();
        var repository = Substitute.For<IRepository>();
        var blob       = Substitute.For<IBlob>();
        repository.GetRepositoryVersionBlob(Arg.Any<RepositoryVersion>()).Returns(blob);
        repository.DownloadAsync(Arg.Any<IBlob>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(Task.FromException(new RequestFailedException(404, "Blob not found", "BlobNotFound", null)));
        mockStorageAccountFactory.GetRepository(Arg.Any<RepositoryOptions>()).Returns(repository);

        SetupFactory(configuration);
    }

    private void GivenLocalFilesystem(ServiceConfiguration configuration)
    {
        mockFileSystem = new MockFileSystem();
        SetupFactory(configuration);
    }

    private void GivenLocalFilesystemWithCachedFile(ServiceConfiguration configuration, string versionName)
    {
        mockFileSystem = new MockFileSystem();
        var cachedFilePath = GetLocalStateDbForRepositoryFullName(configuration, repositoryOptions, new RepositoryVersion { Name = versionName });
        CreateValidSqliteDatabase(cachedFilePath);
        SetupFactory(configuration);
    }

    private void WhenRepositoryIsCreated()
    {
        var mockConfig = Substitute.For<IOptions<AriusConfiguration>>();
        mockConfig.Value.Returns(new AriusConfiguration
        {
            LocalConfigRoot = new DirectoryInfo(@"C:\AriusTest")
            // Setup your configuration here
        });

        var mockLogger = Substitute.For<ILogger<SqliteStateDbRepositoryFactory>>();
        repositoryFactory = new SqliteStateDbRepositoryFactory(mockStorageAccountFactory, mockConfig, mockLogger);

        repositoryOptions = new RepositoryOptions
        {
            AccountName   = "testAccount",
            AccountKey    = "testKey",
            ContainerName = "testContainer",
            Passphrase    = "testPassphrase"
        };
    }

    private void ThenNewFileShouldBeCreatedWithCurrentTimestamp(IStateDbRepository result)
    {
        var expectedFileName = $"{DateTime.UtcNow:s}".Replace(":", "");
        var files            = mockFileSystem.AllFiles;
        files.Should().Contain(f => f.Contains(expectedFileName));
    }

    private void ThenRepositoryVersionShouldBeTheTimestamp(IStateDbRepository result)
    {
        result.Version.Name.Should().Be($"{DateTime.UtcNow:s}".Replace(":", ""));
    }

    private void ThenLatestVersionShouldBeDownloaded(IStateDbRepository result, string expectedVersion)
    {
        result.Version.Name.Should().Be(expectedVersion);
    }

    private void ThenFileShouldBeCachedLocally(IStateDbRepository result)
    {
        var expectedFileName = result.Version.Name.Replace(":", "");
        var files            = mockFileSystem.AllFiles;
        files.Should().Contain(f => f.Contains(expectedFileName));
    }

    private void ThenRepositoryVersionShouldBe(IStateDbRepository result, string expectedVersion)
    {
        result.Version.Name.Should().Be(expectedVersion);
    }

    private void ThenDownloadAsyncShouldBeCalled(ServiceConfiguration configuration)
    {
        if (configuration == ServiceConfiguration.Mocked)
        {
            var repository = mockStorageAccountFactory.GetRepository(repositoryOptions);
            repository.Received(1).DownloadAsync(Arg.Any<IBlob>(), Arg.Any<string>(), Arg.Any<string>());
        }
    }

    private void ThenDownloadAsyncShouldNotBeCalled(ServiceConfiguration configuration)
    {
        if (configuration == ServiceConfiguration.Mocked)
        {
            var repository = mockStorageAccountFactory.GetRepository(repositoryOptions);
            repository.DidNotReceive().DownloadAsync(Arg.Any<IBlob>(), Arg.Any<string>(), Arg.Any<string>());
        }
    }

    private void SetupFactory(ServiceConfiguration configuration)
    {
        if (configuration == ServiceConfiguration.Real)
        {
            // Set up real services here if needed
            throw new NotImplementedException();
        }
    }

    private string GetLocalStateDbForRepositoryFullName(ServiceConfiguration configuration, RepositoryOptions repositoryOptions, RepositoryVersion version)
    {
        return fixture.GetAriusConfiguration(configuration)
            .GetLocalStateDbFolderForRepositoryName(repositoryOptions.ContainerName)
            .GetFullName(version.GetFileSystemName());
    }

    private void CreateValidSqliteDatabase(string filePath)
    {
        var optionsBuilder = new DbContextOptionsBuilder<SqliteStateDbContext>();
        optionsBuilder.UseSqlite($"Data Source={filePath}");

        using var context = new SqliteStateDbContext(optionsBuilder.Options);
        context.Database.EnsureCreated();
        context.Database.Migrate();
    }
}