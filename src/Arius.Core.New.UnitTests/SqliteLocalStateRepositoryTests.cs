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
        return FixtureBuilder.Create()
            //.WithMockedStorageAccountFactory()
            //.WithFakeCryptoService()
            .WithUniqueContainerName()
            .Build();
    }

    protected override void ConfigureOnceForFixture()
    {
    }

    [Fact] public async Task Vacuum_WhenDeletedRecords_SizeSmaller()
    {
        // Arrange
        var repository = await WhenStateDbRepositoryFactoryCreateAsync();

        for (int i = 0; i < 100; i++)
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
        {
            repository.DeletePointerFileEntry(pfe);
        }

        var dbFile = Fixture.AriusConfiguration
            .GetLocalStateDatabaseFolderForRepositoryOptions(Fixture.RemoteRepositoryOptions)
            .GetFiles("*" + IStateDatabaseFile.Extension).Single();
        dbFile.Refresh();
        var originalLength = dbFile.Length;

        // Act
        repository.Vacuum();

        // Assert
        dbFile.Refresh();
        var vacuumedLength = dbFile.Length;

        vacuumedLength.Should().BeLessThan(originalLength);
    }
}