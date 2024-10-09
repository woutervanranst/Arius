using Arius.Core.Domain;
using Arius.Core.Domain.Repositories;
using Arius.Core.Domain.Storage;
using Arius.Core.Domain.Storage.FileSystem;
using Arius.Core.New.UnitTests.Fixtures;
using FluentAssertions;

namespace Arius.Core.New.UnitTests;

public sealed class SqliteLocalStateRepositoryTests : TestBase
{
    protected override AriusFixture GetFixture()
    {
        return new FixtureBuilder()
            .WithRealStorageAccountFactory()
            //.WithFakeCryptoService()
            .WithUniqueContainerName()
            .Build();
    }

    protected override void ConfigureOnceForFixture()
    {
    }

    [Fact]
    public async Task UpsertPointerFileEntry_VariousScenarios()
    {
        // Arrange
        var localStateDatabaseCacheDirectory = Fixture.AriusConfiguration.GetLocalStateDatabaseCacheDirectoryForContainerName(Fixture.RemoteRepositoryOptions.ContainerName);

        var repository = await Fixture.RemoteRepository
            .GetRemoteStateRepository()
            .CreateNewLocalStateRepositoryAsync(localStateDatabaseCacheDirectory, StateVersion.FromName("v1.0"));

        repository.AddBinary(new BinaryProperties
        {
            Hash = "1".StringToBytes(),
            OriginalSize = 10,
            ArchivedSize = 13,
            StorageTier = StorageTier.Hot
            
        });

        // Act
        var r = repository.UpsertPointerFileEntry(new PointerFileEntry
        {
            Hash             = "1".StringToBytes(),
            RelativeName     = "bla",
            CreationTimeUtc  = new DateTime(2020, 05, 20),
            LastWriteTimeUtc = new DateTime(2020, 05, 20)
        });

        // Assert
        r.Should().Be(UpsertResult.Added);

        // Act
        r = repository.UpsertPointerFileEntry(new PointerFileEntry
        {
            Hash             = "1".StringToBytes(),
            RelativeName     = "bla",
            CreationTimeUtc  = new DateTime(2020, 05, 20),
            LastWriteTimeUtc = new DateTime(2020, 05, 20)
        });

        // Assert
        r.Should().Be(UpsertResult.Unchanged);

        // Act
        r = repository.UpsertPointerFileEntry(new PointerFileEntry
        {
            Hash             = "1".StringToBytes(),
            RelativeName     = "bla",
            CreationTimeUtc  = new DateTime(2017, 06, 05),
            LastWriteTimeUtc = DateTime.UtcNow
        });

        // Assert
        r.Should().Be(UpsertResult.Updated);
    }

    }
}