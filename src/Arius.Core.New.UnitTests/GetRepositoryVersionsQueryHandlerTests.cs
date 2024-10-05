using Arius.Core.Domain.Storage;
using Arius.Core.New.Queries.GetStateDbVersions;
using Arius.Core.New.UnitTests.Fixtures;
using FluentAssertions;
using NSubstitute;

namespace Arius.Core.New.UnitTests;

public class GetRepositoryVersionsQueryHandlerTests : TestBase
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

    [Fact]
    public async Task Handle_ShouldReturnRepositoryVersions()
    {
        // Arrange
        string[] versionNames = ["v1.0", "v2.0"];
        var      versions     = versionNames.Select(RepositoryVersion.FromName).ToArray();
        var      repository   = Fixture.RemoteRepository;
        repository.GetRemoteStateRepository().GetRepositoryVersions().Returns(versions.ToAsyncEnumerable());

        var request = new GetRepositoryVersionsQuery { RemoteRepository = Fixture.RemoteRepositoryOptions };

        // Act
        var result = await WhenMediatorRequest(request).ToListAsync();

        // Assert
        result.Select(r => r.Name).Should().ContainInOrder("v1.0", "v2.0");
    }
}