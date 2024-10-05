using Arius.Core.Domain.Storage;
using Arius.Core.New.Commands.Archive;
using Arius.Core.New.UnitTests.Fixtures;
using FluentAssertions;

namespace Arius.Core.New.UnitTests;

public class ArchiveCommandHandlerBlocks_UploadBinaries_Tests : TestBase
{
    protected override AriusFixture GetFixture()
    {
        return new FixtureBuilder()
            .WithRealStorageAccountFactory()
            .WithUniqueContainerName()
            .Build();
    }

    protected override void ConfigureOnceForFixture()
    {
    }

    [Fact]
    public void GetEffectiveStorageTier()
    {
        var c = new ArchiveCommand
        {
            RemoteRepositoryOptions  = Fixture.RemoteRepositoryOptions,
            FastHash    = false,
            RemoveLocal = false,
            Tier        = StorageTier.Hot,
            LocalRoot   = Fixture.TestRootSourceFolder,
            VersionName = StateVersion.FromName("v1.0")
        };
        var st = c.StorageTiering;

        ArchiveCommandHandler.GetEffectiveStorageTier(st, StorageTier.Archive, 1).Should().Be(StorageTier.Cold);
        ArchiveCommandHandler.GetEffectiveStorageTier(st, StorageTier.Archive, 1024 * 1024 + 1).Should().Be(StorageTier.Archive);
    }
}