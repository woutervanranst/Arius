using Arius.Core.Domain;
using Arius.Core.Domain.Storage;
using Arius.Core.New.Commands.Archive;
using Arius.Core.New.UnitTests.Fixtures;

namespace Arius.Core.New.UnitTests;

public class ArchiveCommandTests : TestBase
{
    protected override IAriusFixture ConfigureFixture()
    {
        return FixtureBuilder.Create()
            .WithMockedStorageAccountFactory()
            .WithFakeCryptoService()
            .WithPopulatedSourceFolder()
            .Build();
    }

    [Fact]
    public async Task Handle()
    {
        // Arrange
        var c = new ArchiveCommand
        {
            Repository  = Fixture.RepositoryOptions,
            FastHash    = false,
            RemoveLocal = false,
            Tier        = StorageTier.Hot,
            LocalRoot   = Fixture.SourceFolder,
            VersionName = new RepositoryVersion { Name = "v1.0" }
        };

        // Act
        await WhenMediatorRequest(c);

        // Assert
    }
}