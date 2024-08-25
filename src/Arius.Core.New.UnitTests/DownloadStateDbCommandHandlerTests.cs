using Arius.Core.Domain.Storage;
using Arius.Core.New.Commands.DownloadStateDb;
using Arius.Core.New.UnitTests.Fixtures;
using Azure;
using FluentAssertions;
using NSubstitute;
using System;

namespace Arius.Core.New.UnitTests;

public sealed class DownloadStateDbCommandHandlerTests : IClassFixture<RequestHandlerFixture>
{
    private readonly RequestHandlerFixture fixture;

    public DownloadStateDbCommandHandlerTests(RequestHandlerFixture fixture)
    {
        this.fixture = fixture;
    }

    [Theory]
    [InlineData(ServiceConfiguration.Mocked)]
    [InlineData(ServiceConfiguration.Real)]
    public async Task Handle_WhenNoVersionsAvailable_ShouldReturnNoStatesYet(ServiceConfiguration configuration)
    {
        // Arrange
        var storageAccountFactory = fixture.GetStorageAccountFactory(configuration);
        var mediator              = fixture.GetMediator(configuration);
        var storageAccount        = Substitute.For<IRepository>();

        var localPath = Path.Combine(fixture.UnitTestRoot.FullName, $"test{Random.Shared.Next()}.db");
        var command = new DownloadStateDbCommand
        {
            Repository = fixture.GetRepositoryOptions(configuration),
            LocalPath  = localPath
        };

        if (configuration == ServiceConfiguration.Mocked)
        {
            storageAccountFactory.GetRepository(Arg.Any<RepositoryOptions>()).Returns(storageAccount);
            storageAccount.GetRepositoryVersions().Returns(AsyncEnumerable.Empty<RepositoryVersion>());
        }

        // Act
        var result = await mediator.Send(command);

        // Assert
        result.Type.Should().Be(DownloadStateDbCommandResultType.NoStatesYet);
        result.Version.Should().BeNull();
        File.Exists(localPath).Should().BeFalse();
    }

    [Theory]
    [InlineData(ServiceConfiguration.Mocked)]
    [InlineData(ServiceConfiguration.Real)]
    public async Task Handle_WhenNoSpecificVersionProvided_ShouldDownloadLatestVersion(ServiceConfiguration configuration)
    {
        // Arrange
        var storageAccountFactory = fixture.GetStorageAccountFactory(configuration);
        var mediator              = fixture.GetMediator(configuration);
        var storageAccount        = Substitute.For<IRepository>();

        var localPath = Path.Combine(fixture.UnitTestRoot.FullName, $"test{Random.Shared.Next()}.db");
        var command = new DownloadStateDbCommand
        {
            Repository = fixture.GetRepositoryOptions(configuration),
            LocalPath  = localPath
        };

        if (configuration == ServiceConfiguration.Mocked)
        {
            ConfigureMockRepositoryWithThreeVersions(storageAccountFactory, storageAccount);
        }

        // Act
        var result = await mediator.Send(command);

        // Assert
        result.Type.Should().Be(DownloadStateDbCommandResultType.LatestDownloaded);
        result.Version!.Name.Should().Be("v2.0");
        File.Exists(command.LocalPath).Should().BeTrue();
    }

    [Theory]
    [InlineData(ServiceConfiguration.Mocked)]
    [InlineData(ServiceConfiguration.Real)]
    public async Task Handle_WhenSpecificVersionProvided_ShouldDownloadRequestedVersion(ServiceConfiguration configuration)
    {
        // Arrange
        var storageAccountFactory = fixture.GetStorageAccountFactory(configuration);
        var mediator              = fixture.GetMediator(configuration);
        var storageAccount        = Substitute.For<IRepository>();

        var localPath = Path.Combine(fixture.UnitTestRoot.FullName, $"test{Random.Shared.Next()}.db");
        var command = new DownloadStateDbCommand
        {
            Repository = fixture.GetRepositoryOptions(configuration),
            Version    = new RepositoryVersion { Name = "v1.1" },
            LocalPath  = localPath
        };

        if (configuration == ServiceConfiguration.Mocked)
        {
            ConfigureMockRepositoryWithThreeVersions(storageAccountFactory, storageAccount);
        }

        // Act
        var result = await mediator.Send(command);

        // Assert
        result.Type.Should().Be(DownloadStateDbCommandResultType.RequestedVersionDownloaded);
        result.Version!.Name.Should().Be("v1.1");
        File.Exists(command.LocalPath).Should().BeTrue();
    }

    [Theory]
    [InlineData(ServiceConfiguration.Mocked)]
    [InlineData(ServiceConfiguration.Real)]
    public async Task Handle_WhenSpecificVersionProvidedButDoesNotExist_ShouldReturnException(ServiceConfiguration configuration)
    {
        // Arrange
        var storageAccountFactory = fixture.GetStorageAccountFactory(configuration);
        var mediator              = fixture.GetMediator(configuration);
        var storageAccount        = Substitute.For<IRepository>();

        var localPath = Path.Combine(fixture.UnitTestRoot.FullName, $"test{Random.Shared.Next()}.db");
        var command = new DownloadStateDbCommand
        {
            Repository = fixture.GetRepositoryOptions(configuration),
            Version    = new RepositoryVersion { Name = "v3.0" },
            LocalPath  = localPath
        };

        if (configuration == ServiceConfiguration.Mocked)
        {
            ConfigureMockRepositoryWithThreeVersions(storageAccountFactory, storageAccount);

            var blob = Substitute.For<IBlob>();
            storageAccount.GetRepositoryVersionBlob(Arg.Any<RepositoryVersion>()).Returns(blob);
            blob.OpenReadAsync(Arg.Any<CancellationToken>())
                .Returns(Task.FromException<Stream>(new RequestFailedException(404, "The requested blob was not found", "BlobNotFound", new Exception())));
        }

        // Act
        Func<Task> act = async () => await mediator.Send(command);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
        File.Exists(command.LocalPath).Should().BeFalse();
    }


    private static void ConfigureMockRepositoryWithThreeVersions(IStorageAccountFactory storageAccountFactory, IRepository storageAccount)
    {
        var version1 = new RepositoryVersion { Name = "v1.0" };
        var version2 = new RepositoryVersion { Name = "v1.1" };
        var version3 = new RepositoryVersion { Name = "v2.0" };

        var blob = Substitute.For<IBlob>();

        storageAccountFactory.GetRepository(Arg.Any<RepositoryOptions>()).Returns(storageAccount);
        storageAccount.GetRepositoryVersions().Returns(new[] { version1, version2, version3 }.ToAsyncEnumerable());

        storageAccount.GetRepositoryVersionBlob(Arg.Any<RepositoryVersion>()).Returns(blob);
    }
}