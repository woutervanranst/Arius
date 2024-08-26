using Arius.Core.Domain.Storage;
using Arius.Core.Infrastructure.Repositories;
using Arius.Core.New.UnitTests.Fixtures;
using Azure;
using FluentAssertions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using WouterVanRanst.Utils;

namespace Arius.Core.New.UnitTests;

public sealed class SqliteStateDbRepositoryFactoryTests : IClassFixture<RequestHandlerFixture>
{
    private readonly RequestHandlerFixture fixture;

    public SqliteStateDbRepositoryFactoryTests(RequestHandlerFixture fixture)
    {
        this.fixture = fixture;
    }

    [Theory]
    [InlineData(ServiceConfiguration.Mocked)]
    [InlineData(ServiceConfiguration.Real)]
    public async Task CreateAsync_WhenNoVersionsAvailable_ShouldReturnNewFileName(ServiceConfiguration configuration)
    {
        // Arrange
        var factory           = fixture.GetStateDbRepositoryFactory(configuration);
        var repositoryOptions = fixture.GetRepositoryOptions(configuration);

        if (configuration == ServiceConfiguration.Mocked)
        {
            var repository = fixture.GetStorageAccountFactory(configuration).GetRepository(repositoryOptions);
            repository.GetRepositoryVersions().Returns(AsyncEnumerable.Empty<RepositoryVersion>());
        }

        var startTime = DateTime.UtcNow.TruncateToSeconds();

        // Act
        var result = await factory.CreateAsync(repositoryOptions);

        // Assert
        var endTime = DateTime.UtcNow.TruncateToSeconds();

        DateTime.Parse(result.Version.Name)
            .Should()
            .BeOnOrAfter(startTime).And.BeOnOrBefore(endTime); // Allow for some slack time in the version name comparison

        File.Exists(GetLocalStateDbForRepositoryFullName(configuration, repositoryOptions, result.Version))
            .Should().BeTrue();

        (await result.GetPointerFileEntries().CountAsync()).Should().Be(0);
        (await result.GetBinaryEntries().CountAsync()).Should().Be(0);
    }

    [Theory]
    [InlineData(ServiceConfiguration.Mocked)]
    [InlineData(ServiceConfiguration.Real)]
    public async Task CreateAsync_WhenNoVersionSpecifiedAndLatestVersionNotCached_ShouldDownloadLatestVersion(ServiceConfiguration configuration)
    {
        // Arrange
        var factory           = fixture.GetStateDbRepositoryFactory(configuration);
        var repositoryOptions = fixture.GetRepositoryOptions(configuration);

        if (configuration == ServiceConfiguration.Mocked)
        {
            var latestVersion = new RepositoryVersion { Name = "v2.0" };
            var repository    = fixture.GetStorageAccountFactory(configuration).GetRepository(repositoryOptions);

            // Mocking multiple versions
            repository.GetRepositoryVersions().Returns(new[]
            {
                new RepositoryVersion { Name = "v1.0" },
                new RepositoryVersion { Name = "v1.1" },
                latestVersion
            }.ToAsyncEnumerable());

            var blob = Substitute.For<IBlob>();
            repository.GetRepositoryVersionBlob(latestVersion).Returns(blob);
        }

        // Act
        var result = await factory.CreateAsync(repositoryOptions);

        // Assert
        result.Version.Name.Should().Be("v2.0");

        File.Exists(GetLocalStateDbForRepositoryFullName(configuration, repositoryOptions, result.Version))
            .Should().BeTrue();

        // Verify DownloadAsync was called
        if (configuration == ServiceConfiguration.Mocked)
        {
            var repository = fixture.GetStorageAccountFactory(configuration).GetRepository(repositoryOptions);
            await repository.Received(1).DownloadAsync(Arg.Any<IBlob>(), Arg.Any<string>(), Arg.Any<string>());
        }
    }

    [Theory]
    [InlineData(ServiceConfiguration.Mocked)]
    [InlineData(ServiceConfiguration.Real)]
    public async Task CreateAsync_WhenLatestVersionCached_ShouldReturnCachedRepositoryWithCorrectVersion(ServiceConfiguration configuration)
    {
        // Arrange
        var    factory           = fixture.GetStateDbRepositoryFactory(configuration);
        var    repositoryOptions = fixture.GetRepositoryOptions(configuration);
        string cachedFilePath;

        if (configuration == ServiceConfiguration.Mocked)
        {
            var repository    = fixture.GetStorageAccountFactory(configuration).GetRepository(repositoryOptions);
            var latestVersion = new RepositoryVersion { Name = "v2.0" };
            cachedFilePath = GetLocalStateDbForRepositoryFullName(configuration, repositoryOptions, latestVersion);

            repository.GetRepositoryVersions().Returns(new[]
            {
                new RepositoryVersion { Name = "v1.0" },
                new RepositoryVersion { Name = "v1.1" },
                latestVersion
            }.ToAsyncEnumerable());

            // Create the cached file
            CreateValidSqliteDatabase(cachedFilePath);
            //File.Create(cachedFilePath);
        }
        else
        {
            // Real services
            cachedFilePath = "";
            throw new NotImplementedException();
        }

        // Act
        var result = await factory.CreateAsync(repositoryOptions);

        // Assert
        result.Version.Name.Should().Be("v2.0");

        // Verify DownloadAsync was NOT called since it's cached
        if (configuration == ServiceConfiguration.Mocked)
        {
            var repository = fixture.GetStorageAccountFactory(configuration).GetRepository(repositoryOptions);
            await repository.DidNotReceive().DownloadAsync(Arg.Any<IBlob>(), Arg.Any<string>(), Arg.Any<string>());
        }
    }

    [Theory]
    [InlineData(ServiceConfiguration.Mocked)]
    [InlineData(ServiceConfiguration.Real)]
    public async Task CreateAsync_WhenSpecificVersionNotCached_ShouldDownloadAndCacheSpecificVersion(ServiceConfiguration configuration)
    {
        // Arrange
        var factory           = fixture.GetStateDbRepositoryFactory(configuration);
        var repositoryOptions = fixture.GetRepositoryOptions(configuration);
        var requestedVersion  = new RepositoryVersion { Name = "v1.1" };

        if (configuration == ServiceConfiguration.Mocked)
        {
            var repository = fixture.GetStorageAccountFactory(configuration).GetRepository(repositoryOptions);

            repository.GetRepositoryVersions().Returns(new[]
            {
                new RepositoryVersion { Name = "v1.0" },
                requestedVersion,
                new RepositoryVersion { Name = "v2.0" }
            }.ToAsyncEnumerable());

            var blob = Substitute.For<IBlob>();
            repository.GetRepositoryVersionBlob(requestedVersion).Returns(blob);
        }

        // Act
        var result = await factory.CreateAsync(repositoryOptions, requestedVersion);

        // Assert
        result.Should().NotBeNull();
        result.Version.Should().NotBeNull();
        result.Version.Name.Should().Be("v1.1");

        File.Exists(GetLocalStateDbForRepositoryFullName(configuration, repositoryOptions, result.Version))
            .Should().BeTrue();

        // Verify DownloadAsync was called
        if (configuration == ServiceConfiguration.Mocked)
        {
            var repository = fixture.GetStorageAccountFactory(configuration).GetRepository(repositoryOptions);
            await repository.Received(1).DownloadAsync(Arg.Any<IBlob>(), Arg.Any<string>(), Arg.Any<string>());
        }
    }

    [Theory]
    [InlineData(ServiceConfiguration.Mocked)]
    [InlineData(ServiceConfiguration.Real)]
    public async Task CreateAsync_WhenSpecificVersionCached_ShouldReturnCachedRepositoryWithCorrectVersion(ServiceConfiguration configuration)
    {
        // Arrange
        var factory           = fixture.GetStateDbRepositoryFactory(configuration);
        var repositoryOptions = fixture.GetRepositoryOptions(configuration);
        var requestedVersion  = new RepositoryVersion { Name = "v1.1" };
        var cachedFilePath    = GetLocalStateDbForRepositoryFullName(configuration, repositoryOptions, requestedVersion);

        if (configuration == ServiceConfiguration.Mocked)
        {
            var repository = fixture.GetStorageAccountFactory(configuration).GetRepository(repositoryOptions);

            repository.GetRepositoryVersions().Returns(new[]
            {
                new RepositoryVersion { Name = "v1.0" },
                requestedVersion,
                new RepositoryVersion { Name = "v2.0" }
            }.ToAsyncEnumerable());

            // Create the cached file
            CreateValidSqliteDatabase(cachedFilePath);
        }

        // Act
        var result = await factory.CreateAsync(repositoryOptions, requestedVersion);

        // Assert
        result.Should().NotBeNull();
        result.Version.Should().NotBeNull();
        result.Version.Name.Should().Be("v1.1");

        File.Exists(cachedFilePath).Should().BeTrue();

        // Verify DownloadAsync was NOT called since it's cached
        if (configuration == ServiceConfiguration.Mocked)
        {
            var repository = fixture.GetStorageAccountFactory(configuration).GetRepository(repositoryOptions);
            await repository.DidNotReceive().DownloadAsync(Arg.Any<IBlob>(), Arg.Any<string>(), Arg.Any<string>());
        }
    }

    [Theory]
    [InlineData(ServiceConfiguration.Mocked)]
    [InlineData(ServiceConfiguration.Real)]
    public async Task CreateAsync_WhenSpecificVersionDoesNotExist_ShouldThrowArgumentException(ServiceConfiguration configuration)
    {
        // Arrange
        var factory           = fixture.GetStateDbRepositoryFactory(configuration);
        var repositoryOptions = fixture.GetRepositoryOptions(configuration);
        var requestedVersion  = new RepositoryVersion { Name = "v3.0" };

        if (configuration == ServiceConfiguration.Mocked)
        {
            var repository = fixture.GetStorageAccountFactory(configuration).GetRepository(repositoryOptions);

            var blob = Substitute.For<IBlob>();
            repository.GetRepositoryVersionBlob(requestedVersion).Returns(blob);

            repository.DownloadAsync(Arg.Any<IBlob>(), Arg.Any<string>(), Arg.Any<string>())
                .Returns(Task.FromException(new RequestFailedException(404, "message", "BlobNotFound", null)));
        }

        // Act
        Func<Task> act = async () => await factory.CreateAsync(repositoryOptions, requestedVersion);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>().WithMessage("The requested version was not found*");
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