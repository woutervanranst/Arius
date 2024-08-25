using Arius.Core.Domain.Storage;
using Arius.Core.New.Commands.DownloadStateDb;
using FluentAssertions;
using MediatR;
using NSubstitute;

namespace Arius.Core.New.UnitTests;

public sealed class DownloadStateDbCommandHandlerTests : IClassFixture<CommandHandlerFixture>
{
    private readonly CommandHandlerFixture fixture;

    public DownloadStateDbCommandHandlerTests(CommandHandlerFixture fixture)
    {
        this.fixture = fixture;
    }

    //[Fact]
    //public async Task Handle_DownloadLatestStateDbCommand_ShouldReturnUnit_WhenLatestVersionIsNull()
    //{
    //    // Arrange
    //    var localPath = @"c:\ariustest\wouter.db";
    //    File.Exists(localPath).Should().BeFalse();
    //    var command = new DownloadLatestStateDbCommand
    //    {
    //        Repository = new RepositoryOptions
    //        {
    //            AccountName   = fixture.TestRepositoryOptions.AccountName,
    //            AccountKey    = fixture.TestRepositoryOptions.AccountKey,
    //            ContainerName = fixture.TestRepositoryOptions.ContainerName,
    //            Passphrase    = fixture.TestRepositoryOptions.Passphrase
    //        },
    //        LocalPath = localPath
    //    };

    //    var repository = Substitute.For<IRepository>();
    //    repository.GetRepositoryVersions().Returns(AsyncEnumerable.Empty<RepositoryVersion>());

    //    fixture.StorageAccountFactory.GetRepository(command.Repository).Returns(repository);

    //    // Act
    //    var result = await fixture.Mediator.Send(command, CancellationToken.None);

    //    // Assert
    //    result.Should().Be(Unit.Value);
    //    File.Exists(localPath).Should().BeTrue();
    //}

    [Theory]
    [InlineData(ServiceConfiguration.Mocked)]
    [InlineData(ServiceConfiguration.Real)]
    public async Task Handle_DownloadStateDbCommand_ShouldDownloadBlob(ServiceConfiguration configuration)
    {
        // Arrange
        var storageAccountFactory = fixture.GetStorageAccountFactory(configuration);
        var mediator              = fixture.GetMediator(configuration);

        var localPath = Path.Combine(fixture.UnitTestRoot.FullName, $"test{Random.Shared.Next()}.db");
        var command = new DownloadLatestStateDbCommand
        {
            Repository = fixture.GetRepositoryOptions(configuration),
            LocalPath = localPath
        };

        if (configuration == ServiceConfiguration.Mocked)
        {
            var latestVersion = new RepositoryVersion { Name = "v2.0" };
            var repository    = Substitute.For<IRepository>();
            var blob          = Substitute.For<IBlob>();

            repository.GetRepositoryVersions().Returns(new[] { latestVersion }.ToAsyncEnumerable());
            repository.GetRepositoryVersionBlob(latestVersion).Returns(blob);

            storageAccountFactory.GetRepository(command.Repository).Returns(repository);
        }

        // Act
        var result = await mediator.Send(command, CancellationToken.None);

        // Assert
        result.Should().Be(Unit.Value);
        File.Exists(localPath).Should().BeTrue();
    }
}