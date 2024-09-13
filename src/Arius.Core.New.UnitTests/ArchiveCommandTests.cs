using Arius.Core.Domain.Storage;
using Arius.Core.New.Commands.Archive;
using Arius.Core.New.Queries.RepositoryStatistics;
using Arius.Core.New.UnitTests.Fixtures;

namespace Arius.Core.New.UnitTests;

public class ArchiveCommandTests : TestBase
{
    protected override AriusFixture GetFixture()
    {
        return FixtureBuilder.Create()
            .WithUniqueContainerName()
            //.WithMockedStorageAccountFactory()
            //.WithFakeCryptoService()
            .Build();
    }

    protected override void ConfigureOnceForFixture()
    {
        GivenPopulatedSourceFolder();
    }

    [Fact]
    public async Task Handle()
    {
        // Arrange
        var q = new RepositoryStatisticsQuery
        {
            RemoteRepository = Fixture.RemoteRepositoryOptions
        };
        var s0 = await WhenMediatorRequest(q);

        var c = new ArchiveCommand
        {
            FastHash    = false,
            RemoveLocal = false,
            Tier        = StorageTier.Hot,
            LocalRoot   = Fixture.TestRunSourceFolder,
            VersionName = new RepositoryVersion { Name = "v1.0" }
        };

        // Act
        await WhenMediatorRequest(c);

        // Assert

        var s1 = await WhenMediatorRequest(q);
    }
    }
}