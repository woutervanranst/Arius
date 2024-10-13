using Arius.Core.Domain;
using Arius.Core.Domain.Repositories;
using Arius.Core.Domain.Storage;
using Arius.Core.Domain.Storage.FileSystem;
using Arius.Core.Infrastructure.Repositories;
using Arius.Core.Infrastructure.Storage.LocalFileSystem;
using Arius.Core.New.Commands.Archive;
using Arius.Core.New.Queries.ContainerNames;
using Arius.Core.New.Queries.GetStateDbVersions;
using Arius.Core.New.Queries.RepositoryStatistics;
using Arius.Core.Tests.Extensions;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using System.Linq.Expressions;
using File = System.IO.File;

namespace Arius.Core.Tests.Fixtures;

public abstract class TestBase
{
    protected AriusFixture Fixture { get; }

    protected TestBase()
    {
        Fixture = GetFixture();
        ConfigureOnceForFixture();
    }

    protected abstract AriusFixture GetFixture();
    protected abstract void ConfigureOnceForFixture();


    // --- GIVEN

    //protected IFileSystem GivenLocalFilesystem()
    //{
    //    return Fixture.LocalFileSystem;
    //}

    //protected void GivenLocalFilesystemWithVersions(string[] versionNames)
    //{
    //    var repository = Fixture.RemoteRepository;
    //    var versions   = versionNames.Select(StateVersion.FromName).ToArray();
    //    repository.GetRemoteStateRepository().GetStateVersions().Returns(versions.ToAsyncEnumerable());

    //    foreach (var versionName in versionNames)
    //    {
    //        var version = StateVersion.FromName(versionName);
    //        var sdbf    = GetStateDatabaseFileForRepository(Fixture, version);

    //        CreateLocalDatabase(sdbf);
    //    }
    //}

    protected void GivenAzureStorageAccountWithContainers(params string[] containerNames)
    {
        var storageAccount = Substitute.For<IStorageAccount>();
        Fixture.StorageAccountFactory.GetStorageAccount(Arg.Any<StorageAccountOptions>()).Returns(storageAccount);

        var containers = containerNames.Select(n => Substitute.For<IContainer>().With(c => c.Name.Returns(n)));
        storageAccount.GetContainers(Arg.Any<CancellationToken>())
            .Returns(containers.ToAsyncEnumerable());
    }

    //protected void GivenAzureRepositoryWithNoVersions()
    //{
    //    var repository = Fixture.RemoteRepository;

    //    if (repository.IsSubstitute())
    //    {
    //        repository.GetRemoteStateRepository().GetStateVersions().Returns(AsyncEnumerable.Empty<StateVersion>());
    //    }
    //    else
    //    {
    //        // Nothing
    //    }
    //}

    //protected void GivenAzureRepositoryWithVersions(string[] versionNames)
    //{
    //    var repository = Fixture.RemoteRepository;
    //    var versions   = versionNames.Select(StateVersion.FromName).ToArray();
    //    repository.GetRemoteStateRepository().GetStateVersions().Returns(versions.ToAsyncEnumerable());

    //    // Set up the repository to throw an exception for versions not in the list
    //    //repository.GetStateDatabaseBlobForVersion(Arg.Any<StateVersion>())
    //    //    .Returns(info =>
    //    //    {
    //    //        var requestedVersion = info.Arg<StateVersion>();
    //    //        if (!versionNames.Contains(requestedVersion.Name))
    //    //        {
    //    //            throw new RequestFailedException(404, "Blob not found", "BlobNotFound", null);
    //    //        }

    //    //        return Substitute.For<IBlob>();
    //    //    });

    //    //repository.DownloadAsync(Arg.Any<IBlob>(), Arg.Any<IFile>(), Arg.Any<CancellationToken>())
    //    //    .Returns(info =>
    //    //    {
    //    //        var blob = info.Arg<IBlob>();
    //    //        if (blob == null)
    //    //        {
    //    //            return Task.FromException(new RequestFailedException(404, "Blob not found", "BlobNotFound", null));
    //    //        }
    //    //        else
    //    //        {
    //    //            var sdbf = info.Arg<IFile>() as StateDatabaseFile;
    //    //            CreateLocalDatabase(sdbf);
    //    //        }

    //    //        return Task.CompletedTask;
    //    //    });
    //}

    public void GivenPopulatedSourceFolder()
    {
        Fixture.TestRootSourceFolder.CopyTo(Fixture.TestRunSourceFolder, recursive: true);
    }

    internal FilePairWithHash GivenSourceFolderHavingFilePair(string relativeName, FilePairType type, int sizeInBytes, FileAttributes attributes = FileAttributes.Normal)
    {
        var pfs = new PointerFileSerializer(NullLogger<PointerFileSerializer>.Instance);

        IBinaryFileWithHash?  bfwh = null;
        IPointerFileWithHash? pfwh = null;

        switch (type)
        {
            case FilePairType.BinaryFileWithPointerFile:
                bfwh      = GetBinaryFileWithHash();
                (_, pfwh) = pfs.CreateIfNotExists(bfwh);

                return FilePairWithHash.FromFilePair(pfwh, bfwh);
            case FilePairType.PointerFileOnly:
                pfwh = GetPointerFileWithHash();

                return FilePairWithHash.FromPointerFile(pfwh);
            case FilePairType.BinaryFileOnly:
                bfwh = GetBinaryFileWithHash();
                
                return FilePairWithHash.FromBinaryFile(bfwh);
            default:
                throw new InvalidOperationException("Must have either a binary file or a pointer file");
        }

        IBinaryFileWithHash GetBinaryFileWithHash()
        {
            var bf = BinaryFile.FromRelativeName(Fixture.TestRunSourceFolder, relativeName);
            FileUtils.CreateRandomFile(bf.FullName, sizeInBytes);
            SetAttributes(bf.FullName, attributes);

            var h = Fixture.HashValueProvider.GetHashAsync(bf).Result;
            return BinaryFileWithHash.FromBinaryFile(bf, h);
        }

        IPointerFileWithHash GetPointerFileWithHash()
        {
            var randomBytes = new byte[32];
            Random.Shared.NextBytes(randomBytes);
            var h = new Hash(randomBytes);

            return pfs
                .CreateIfNotExists(Fixture.TestRunSourceFolder, relativeName, h, DateTime.UtcNow, DateTime.UtcNow)
                .PointerFileWithHash;
        }
    }

    private static void SetAttributes(string filePath, FileAttributes attributes)
    {
        File.SetAttributes(filePath, attributes);

        var actualAttrs = File.GetAttributes(filePath);
        if (actualAttrs != attributes)
            throw new InvalidOperationException($"Could not set attributes for {filePath}");
    }

    internal FilePairWithHash GivenSourceFolderHavingCopyOfFilePair(FilePairWithHash original, string copyRelativeName)
    {
        switch (original.Type)
        {
            case FilePairType.BinaryFileWithPointerFile:
                throw new NotImplementedException();
            case FilePairType.PointerFileOnly:
                throw new NotImplementedException();
            case FilePairType.BinaryFileOnly:
                var bfi0 = new FileInfo(original.BinaryFile.FullName);
                var bfi1 = bfi0.CopyTo(Fixture.TestRunSourceFolder, copyRelativeName);
                SetAttributes(bfi1.FullName, bfi0.Attributes);

                var bf2 = BinaryFileWithHash.FromRelativeName(Fixture.TestRunSourceFolder, copyRelativeName, original.Hash);
                return FilePairWithHash.FromBinaryFile(bf2);
            default:
                throw new InvalidOperationException("Must have either a binary file or a pointer file");
        }
    }


    // --- WHEN

    //protected async Task<ILocalStateRepository> WhenGetLocalStateRepositoryAsync(string? versionName = null)
    //{
    //    return await CreateNewLocalStateRepositoryAsync(versionName);
    //}

    protected IAsyncEnumerable<string> WhenMediatorRequest(ContainerNamesQuery request)
    {
        return Fixture.Mediator.CreateStream(request);
    }

    protected IAsyncEnumerable<StateVersion> WhenMediatorRequest(GetRepositoryVersionsQuery request)
    {
        return Fixture.Mediator.CreateStream(request);
    }

    //protected async Task WhenArchiveCommandAsync(ArchiveCommand request)
    //{
    //    await Fixture.Mediator.Send(request);
    //}

    //protected async Task WhenArchiveCommandAsync(bool fastHash, bool removeLocal, StorageTier tier)
    //{
    //    var c = new ArchiveCommand
    //    {
    //        RemoteRepositoryOptions = Fixture.RemoteRepositoryOptions,
    //        FastHash                = fastHash,
    //        RemoveLocal             = removeLocal,
    //        Tier                    = tier,
    //        LocalRoot               = Fixture.TestRunSourceFolder,
    //    };

    //    await Fixture.Mediator.Send(c);
    //}

    protected async Task WhenArchiveCommandAsync(bool fastHash, bool removeLocal, StorageTier tier, string versionName)
    {
        ArgumentNullException.ThrowIfNullOrWhiteSpace(versionName);

        var version = StateVersion.FromName(versionName);

        await WhenArchiveCommandAsync(fastHash, removeLocal, tier, version);
    }

    protected async Task WhenArchiveCommandAsync(bool fastHash, bool removeLocal, StorageTier tier, StateVersion version)
    {
        var c = new ArchiveCommand
        {
            RemoteRepositoryOptions = Fixture.RemoteRepositoryOptions,
            FastHash                = fastHash,
            RemoveLocal             = removeLocal,
            Tier                    = tier,
            LocalRoot               = Fixture.TestRunSourceFolder,
            Version                 = version
        };

        await Fixture.Mediator.Send(c);
    }

    protected async Task<RepositoryStatisticsQueryResponse> GetRepositoryStatisticsAsync()
    {
        return await Fixture.Mediator.Send(new RepositoryStatisticsQuery { RemoteRepository = Fixture.RemoteRepositoryOptions });
    }

    //protected async Task<TResponse> WhenMediatorRequest<TResponse>(IRequest<TResponse> request)
    //{
    //    return await Fixture.Mediator.Send(request);
    //}


    // --- THEN

    //protected void ThenStateDbVersionShouldBe(ILocalStateRepository repository, string expectedVersion)
    //{
    //    repository.Version.Name.Should().Be(expectedVersion);
    //}

    //protected void ThenStateDbVersionShouldBeBetween(StateVersion version, DateTime startTime, DateTime endTime)
    //{
    //    DateTime.Parse(version.Name)
    //        .Should()
    //        .BeOnOrAfter(startTime).And.BeOnOrBefore(endTime);  // TODO use Should().BeCloseTo
    //}

    //protected void ThenLocalStateDbsShouldExist(
    //    string[]? cachedVersions = null,
    //    int? cachedVersionCount = null, 
    //    int? distinctCount = null)
    //{
    //    var dbfs = GetAllLocalStateVersions(Fixture).ToArray();

    //    if (cachedVersionCount is not null)
    //        dbfs.Length.Should().Be(cachedVersionCount);
    //    if (distinctCount is not null)
    //        dbfs.Select(dbf => dbf.Version.Name).Distinct().Count().Should().Be(distinctCount);

    //    if (cachedVersions is not null)
    //        dbfs.Select(dbf => dbf.Version.Name).Should().BeEquivalentTo(cachedVersions);
    //}

    //protected void ThenStateDbShouldBeEmpty(ILocalStateRepository localStateRepository)
    //{
    //    localStateRepository.CountPointerFileEntries().Should().Be(0);
    //    localStateRepository.CountBinaryProperties().Should().Be(0);
    //}

    //protected void ThenDownloadShouldNotHaveBeenCalled()
    //{
    //    throw new NotImplementedException();

    //    var repository = Fixture.StorageAccountFactory.GetRemoteRepository(Fixture.RemoteRepositoryOptions);
    //    //repository.DidNotReceive().DownloadAsync(Arg.Any<IBlob>(), Arg.Any<IFile>(), Arg.Any<CancellationToken>());
    //}

    //protected void ThenDownloadShouldHaveBeenCalled()
    //{
    //    throw new NotImplementedException();

    //    var repository = Fixture.RemoteRepository;
    //    //repository.Received(1).DownloadAsync(Arg.Any<IBlob>(), Arg.Any<IFile>(), Arg.Any<CancellationToken>());
    //}

    //protected async Task ThenArgumentExceptionShouldBeThrownAsync(Func<Task> act, string expectedMessagePart)
    //{
    //    await act.Should().ThrowAsync<ArgumentException>()
    //        .WithMessage($"*{expectedMessagePart}*");
    //}


    protected void ThenShouldContainMediatorNotification<TNotification>() 
        => Fixture.MediatorNotifications.OfType<TNotification>().Should().NotBeEmpty();

    protected void ThenShouldNotContainMediatorNotification<TNotification>()
        => Fixture.MediatorNotifications.OfType<TNotification>().Should().BeEmpty();

    protected void ThenShouldContainMediatorNotification<TNotification>(Expression<Func<TNotification, bool>> predicate) 
        => Fixture.MediatorNotifications.OfType<TNotification>().Should().Contain(predicate);
    
    protected void ThenShouldContainMediatorNotification<TNotification>(Expression<Func<TNotification, bool>> predicate, out TNotification notification) 
        => notification = Fixture.MediatorNotifications.OfType<TNotification>().AsQueryable().Single(predicate);


    // --- HELPERS

    //protected async Task<ILocalStateRepository> CreateNewLocalStateRepositoryAsync(string versionName)
    //{
    //    var localStateDatabaseCacheDirectory = Fixture.AriusConfiguration.GetLocalStateDatabaseCacheDirectoryForContainerName(Fixture.RemoteRepositoryOptions.ContainerName);
    //    var version                          = StateVersion.FromName(versionName);

    //    return await Fixture.RemoteStateRepository.CreateNewLocalStateRepositoryAsync(localStateDatabaseCacheDirectory, version);
    //}

    protected async Task<ILocalStateRepository> GetLocalStateRepositoryAsync(string? versionName = null)
    {
        var localStateDatabaseCacheDirectory = Fixture.AriusConfiguration.GetLocalStateDatabaseCacheDirectoryForContainerName(Fixture.RemoteRepositoryOptions.ContainerName);
        var version                          = versionName != null ? StateVersion.FromName(versionName) : null;

        return await Fixture.RemoteStateRepository.GetLocalStateRepositoryAsync(localStateDatabaseCacheDirectory, version)
               ?? throw new InvalidOperationException();
    }


    //private IStateDatabaseFile GetStateDatabaseFileForRepository(AriusFixture fixture, StateVersion version)
    //{
    //    return StateDatabaseFile.FromRepositoryVersion(GetLocalStateDatabaseFolder(fixture), version);
    //}

    public IEnumerable<StateVersion> GetAllLocalStateVersions()
    {
        var stateDbFolder = Fixture.AriusConfiguration.GetLocalStateDatabaseCacheDirectoryForContainerName(Fixture.RemoteRepositoryOptions.ContainerName);
        foreach (var fi in stateDbFolder
                     .GetFiles("*.*", SearchOption.AllDirectories)
                     .Where(fi => fi.Name.EndsWith(IStateDatabaseFile.Extension)))
        {
            var n = System.IO.Path.GetFileName(fi.FullName).RemoveSuffix(IStateDatabaseFile.Extension);
            yield return StateVersion.FromName(n);
            //var version = StateVersion.FromName(n);
            //yield return StateDatabaseFile.FromRepositoryVersion(stateDbFolder, version);
        }
    }

    public IAsyncEnumerable<StateVersion> GetAllRemoteStateVersions()
    {
        return Fixture.RemoteStateRepository.GetStateVersions();
    }

    //private DirectoryInfo GetLocalStateDatabaseFolder(AriusFixture fixture)
    //{
    //    return fixture.AriusConfiguration
    //        .GetLocalStateDatabaseCacheDirectoryForContainerName(fixture.RemoteRepositoryOptions.ContainerName);
    //}


    protected static void CreateLocalDatabase(IStateDatabaseFile sdbf)
    {
        var optionsBuilder = new DbContextOptionsBuilder<SqliteStateDatabaseContext>();
        optionsBuilder.UseSqlite($"Data Source={sdbf.FullName}");

        using var context = new SqliteStateDatabaseContext(optionsBuilder.Options, _ => {});
        context.Database.Migrate();
    }

    protected static void CreateLocalDatabaseWithEntry(
        DirectoryInfo stateDbFolder, 
        StateVersion version,
        IEnumerable<string> binaryPropertiesHashes)
    {
        var sdbf = StateDatabaseFile.FromRepositoryVersion(stateDbFolder, version);

        var optionsBuilder = new DbContextOptionsBuilder<SqliteStateDatabaseContext>();
        optionsBuilder.UseSqlite($"Data Source={sdbf.FullName}");

        using var context = new SqliteStateDatabaseContext(optionsBuilder.Options, _ => { });
        context.Database.Migrate();

        foreach (var hash in binaryPropertiesHashes)
            context.BinaryProperties.Add(new BinaryPropertiesDto { Hash = hash.StringToBytes() }); // add a marker

        context.SaveChanges();

        SqliteConnection.ClearAllPools();
    }

    protected static void LocalDatabaseHasEntry(ILocalStateRepository localStateRepository, string binaryPropertiesHash)
    {
        localStateRepository.GetBinaryProperties().Should().Contain(bp => bp.Hash == binaryPropertiesHash.StringToBytes());
    }
}