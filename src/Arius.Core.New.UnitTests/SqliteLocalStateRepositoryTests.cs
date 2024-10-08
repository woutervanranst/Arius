using Arius.Core.Domain;
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

    [Fact] public async Task Vacuum_WhenDeletedRecords_SizeSmaller()
    [Fact]
    public async Task UpsertPointerFileEntry()
    {
        // Arrange
        var repository = await CreateNewLocalStateRepositoryAsync("v1.0");
        var localStateDatabaseCacheDirectory = Fixture.AriusConfiguration.GetLocalStateDatabaseCacheDirectoryForContainerName(Fixture.RemoteRepositoryOptions.ContainerName);

        var repository = await Fixture.RemoteRepository
            .GetRemoteStateRepository()
            .CreateNewLocalStateRepositoryAsync(localStateDatabaseCacheDirectory, StateVersion.FromName("v1.0"));

        for (int i = 0; i < 100; i++)
        repository.AddBinary(new BinaryProperties
        {
            repository.AddBinary(new BinaryProperties()
            {
                Hash = new Hash(i.ToString().StringToBytes()),
                ArchivedSize= 100,
                StorageTier = StorageTier.Hot,
                OriginalSize = 200

            });
            repository.AddPointerFileEntry(new PointerFileEntry()
            {
                Hash = new Hash(i.ToString().StringToBytes()),
                RelativeName = "bla",
                CreationTimeUtc = DateTime.UtcNow,
                LastWriteTimeUtc = DateTime.UtcNow
            });
        }

        foreach (var pfe in repository.GetPointerFileEntries())
            Hash = "1".StringToBytes(),
            OriginalSize = 10,
            ArchivedSize = 13,
            StorageTier = StorageTier.Hot
            
        });

        // Act
        var r = repository.UpsertPointerFileEntry(new PointerFileEntry
        {
            repository.DeletePointerFileEntry(pfe);
        }
            Hash             = "1".StringToBytes(),
            RelativeName     = "bla",
            CreationTimeUtc  = new DateTime(2020, 05, 20),
            LastWriteTimeUtc = new DateTime(2020, 05, 20)
        });

        var dbFile = Fixture.AriusConfiguration
            .GetLocalStateDatabaseCacheDirectoryForContainerName(Fixture.RemoteRepositoryOptions.ContainerName)
            .GetFiles("*" + IStateDatabaseFile.Extension).Single();
        dbFile.Refresh();
        var originalLength = dbFile.Length;
        // Assert
        r.Should().Be(UpsertResult.Added);

        // Act
        repository.Vacuum();
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
        dbFile.Refresh();
        var vacuumedLength = dbFile.Length;
        r.Should().Be(UpsertResult.Updated);
    }

        vacuumedLength.Should().BeLessThan(originalLength);
    }
}