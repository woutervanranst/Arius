using Arius.Core.Domain;
using Arius.Core.Domain.Repositories;
using Arius.Core.Domain.Storage;
using Arius.Core.Tests.Fixtures;
using FluentAssertions;

namespace Arius.Core.Tests;

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

    [Fact]
    public async Task Vacuum_WhenDeletedRecords_SizeSmaller()
    {
        throw new NotImplementedException();

        //// Arrange
        //var repository = await CreateNewLocalStateRepositoryAsync("v1.0");

        //for (var i = 0; i < 100; i++)
        //{
        //    repository.AddBinary(new BinaryProperties()
        //    {
        //        Hash = new Hash(i.ToString().StringToBytes()),
        //        ArchivedSize= 100,
        //        StorageTier = StorageTier.Hot,
        //        OriginalSize = 200

        //    });
        //    repository.AddPointerFileEntry(new PointerFileEntry()
        //    {
        //        Hash = new Hash(i.ToString().StringToBytes()),
        //        RelativeName = "bla",
        //        CreationTimeUtc = DateTime.UtcNow,
        //        LastWriteTimeUtc = DateTime.UtcNow
        //    });
        //}

        //foreach (var pfe in repository.GetPointerFileEntries())
        //{
        //    repository.DeletePointerFileEntry(pfe);
        //}

        //var dbFile = Fixture.AriusConfiguration
        //    .GetLocalStateDatabaseCacheDirectoryForContainerName(Fixture.RemoteRepositoryOptions.ContainerName)
        //    .GetFiles("*" + IStateDatabaseFile.Extension).Single();
        //dbFile.Refresh();
        //var originalLength = dbFile.Length;

        //// Act
        //repository.Vacuum();

        //// Assert
        //dbFile.Refresh();
        //var vacuumedLength = dbFile.Length;

        //vacuumedLength.Should().BeLessThan(originalLength);
    }
}