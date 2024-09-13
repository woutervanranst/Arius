using Arius.Core.Domain;
using Arius.Core.Domain.Repositories;
using Arius.Core.Domain.Storage;
using Arius.Core.Domain.Storage.FileSystem;
using Arius.Core.Infrastructure.Repositories;
using Arius.Core.New.Commands.Archive;
using Arius.Core.New.Queries.ContainerNames;
using Arius.Core.New.Queries.GetStateDbVersions;
using Arius.Core.New.Queries.RepositoryStatistics;
using Arius.Core.New.UnitTests.Extensions;
using Azure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using File = System.IO.File;

namespace Arius.Core.New.UnitTests.Fixtures;

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

    protected void GivenLocalFilesystem()
    {
        // No need to initialize anything
    }

    protected void GivenLocalFilesystemWithVersions(string[] versionNames)
    {
        var repository = Fixture.RemoteRepository;
        var versions   = versionNames.Select(name => new RepositoryVersion { Name = name }).ToArray();
        repository.GetStateDatabaseVersions().Returns(versions.ToAsyncEnumerable());

        foreach (var versionName in versionNames)
        {
            var version    = new RepositoryVersion { Name = versionName };
            var sdbf = GetStateDatabaseFileForRepository(Fixture, version, false); //isTemp = false - we 'pretend' these are cached files

            CreateLocalDatabase(sdbf);
        }
    }

    protected void GivenAzureStorageAccountWithContainers(params string[] containerNames)
    {
        var storageAccount = Substitute.For<IStorageAccount>();
        Fixture.StorageAccountFactory.GetStorageAccount(Arg.Any<StorageAccountOptions>()).Returns(storageAccount);

        var containers = containerNames.Select(n =>
        {
            var c = Substitute.For<IContainer>();
            c.Name.Returns(n);

            return c;
        });
        storageAccount.GetContainers(Arg.Any<CancellationToken>())
            .Returns(containers.ToAsyncEnumerable());
    }

    protected void GivenAzureRepositoryWithNoVersions()
    {
        var repository = Fixture.RemoteRepository;
        repository.GetStateDatabaseVersions().Returns(AsyncEnumerable.Empty<RepositoryVersion>());
    }

    protected void GivenAzureRepositoryWithVersions(string[] versionNames)
    {
        var repository = Fixture.RemoteRepository;
        var versions   = versionNames.Select(name => new RepositoryVersion { Name = name }).ToArray();
        repository.GetStateDatabaseVersions().Returns(versions.ToAsyncEnumerable());

        // Set up the repository to throw an exception for versions not in the list
        repository.GetStateDatabaseBlobForVersion(Arg.Any<RepositoryVersion>())
            .Returns(info =>
            {
                var requestedVersion = info.Arg<RepositoryVersion>();
                if (!versionNames.Contains(requestedVersion.Name))
                {
                    throw new RequestFailedException(404, "Blob not found", "BlobNotFound", null);
                }

                return Substitute.For<IBlob>();
            });

        repository.DownloadAsync(Arg.Any<IBlob>(), Arg.Any<IFile>(), Arg.Any<CancellationToken>())
            .Returns(info =>
            {
                var blob = info.Arg<IBlob>();
                if (blob == null)
                {
                    return Task.FromException(new RequestFailedException(404, "Blob not found", "BlobNotFound", null));
                }
                else
                {
                    var sdbf = info.Arg<IFile>() as StateDatabaseFile;
                    CreateLocalDatabase(sdbf);
                }

                return Task.CompletedTask;
            });
    }

    public void GivenPopulatedSourceFolder()
    {
        Fixture.TestRootSourceFolder.CopyTo(Fixture.TestRunSourceFolder, recursive: true);
    }

    internal FilePairWithHash GivenSourceFolderHavingFilePair(string relativeName, FilePairType type, int sizeInBytes, FileAttributes attributes = FileAttributes.Normal)
    {
        BinaryFileWithHash?  bfwh = null;
        PointerFileWithHash? pfwh = null;

        switch (type)
        {
            case FilePairType.BinaryFileWithPointerFile:
                bfwh = GetBinaryFileWithHash();
                pfwh = PointerFileWithHash.Create(bfwh);
                break;
            case FilePairType.PointerFileOnly:
                pfwh = GetPointerFileWithHash();
                break;
            case FilePairType.BinaryFileOnly:
                bfwh = GetBinaryFileWithHash();
                break;
            default:
                throw new InvalidOperationException("Must have either a binary file or a pointer file");
        }

        return new(pfwh, bfwh);

        static void SetAttributes(FileAttributes attributes, string filePath)
        {
            File.SetAttributes(filePath, attributes);

            var actualAtts = File.GetAttributes(filePath);
            if (actualAtts != attributes)
                throw new InvalidOperationException($"Could not set attributes for {filePath}");
        }

        BinaryFileWithHash GetBinaryFileWithHash()
        {
            var bf = BinaryFile.FromRelativeName(Fixture.TestRunSourceFolder, relativeName);
            FileUtils.CreateRandomFile(bf.FullName, sizeInBytes);
            SetAttributes(attributes, bf.FullName);

            var h = Fixture.HashValueProvider.GetHashAsync(bf).Result;
            return BinaryFileWithHash.FromBinaryFile(bf, h);
        }

        PointerFileWithHash GetPointerFileWithHash()
        {
            var randomBytes = new byte[32];
            Random.Shared.NextBytes(randomBytes);
            var h = new Hash(randomBytes);

            return PointerFileWithHash.Create(Fixture.TestRunSourceFolder, relativeName, h, DateTime.UtcNow, DateTime.UtcNow);
        }
    }


    // --- WHEN

    protected async Task<ILocalStateRepository> WhenStateDbRepositoryFactoryCreateAsync(string? versionName = null)
    {
        var factory           = Fixture.RemoteStateRepository;
        var repositoryOptions = Fixture.RemoteRepositoryOptions;
        var version           = versionName != null ? new RepositoryVersion { Name = versionName } : null;
        return await factory.CreateAsync(repositoryOptions, version);
    }

    protected IAsyncEnumerable<string> WhenMediatorRequest(ContainerNamesQuery request)
    {
        return Fixture.Mediator.CreateStream(request);
    }

    protected IAsyncEnumerable<RepositoryVersion> WhenMediatorRequest(GetRepositoryVersionsQuery request)
    {
        return Fixture.Mediator.CreateStream(request);
    }

    protected async Task WhenMediatorRequest(ArchiveCommand request)
    {
        await Fixture.Mediator.Send(request);
    }

    protected async Task<RepositoryStatisticsQueryResponse> WhenMediatorRequest(RepositoryStatisticsQuery request)
    {
        return await Fixture.Mediator.Send(request);
    }

    //protected async Task<TResponse> WhenMediatorRequest<TResponse>(IRequest<TResponse> request)
    //{
    //    return await Fixture.Mediator.Send(request);
    //}


    // --- THEN

    protected void ThenStateDbVersionShouldBe(ILocalStateRepository repository, string expectedVersion)
    {
        repository.Version.Name.Should().Be(expectedVersion);
    }

    protected void ThenStateDbVersionShouldBeBetween(RepositoryVersion version, DateTime startTime, DateTime endTime)
    {
        DateTime.Parse(version.Name)
            .Should()
            .BeOnOrAfter(startTime).And.BeOnOrBefore(endTime);
    }

    protected void ThenLocalStateDbsShouldExist(string[]? tempVersions = null, string[]? cachedVersions = null, 
        int? tempVersionCount = null, int? cachedVersionCount = null, int? distinctCount = null)
    {
        var dbfs   = GetAllStateDatabaseFilesForRepository(Fixture).ToArray();
        var temp   = dbfs.Where(dbf => dbf.IsTemp).ToArray();
        var cached = dbfs.Where(dbf => !dbf.IsTemp).ToArray();

        if (tempVersionCount is not null)
            temp.Length.Should().Be(tempVersionCount);
        if (cachedVersionCount is not null)
            cached.Length.Should().Be(cachedVersionCount);
        if (distinctCount is not null)
            dbfs.Select(dbf => dbf.Version.Name).Distinct().Count().Should().Be(distinctCount);

        if (tempVersions is not null)
            temp.Select(dbf => dbf.Version.Name).Should().BeEquivalentTo(tempVersions);
        if (cachedVersions is not null)
            cached.Select(dbf => dbf.Version.Name).Should().BeEquivalentTo(cachedVersions);
    }

    protected void ThenStateDbShouldBeEmpty(ILocalStateRepository localStateRepository)
    {
        localStateRepository.CountPointerFileEntries().Should().Be(0);
        localStateRepository.CountBinaryProperties().Should().Be(0);
    }

    protected void ThenDownloadShouldNotHaveBeenCalled()
    {
        var repository = Fixture.StorageAccountFactory.GetRemoteRepository(Fixture.RemoteRepositoryOptions);
        repository.DidNotReceive().DownloadAsync(Arg.Any<IBlob>(), Arg.Any<IFile>(), Arg.Any<CancellationToken>());
    }

    protected void ThenDownloadShouldHaveBeenCalled()
    {
        var repository = Fixture.RemoteRepository;
        repository.Received(1).DownloadAsync(Arg.Any<IBlob>(), Arg.Any<IFile>(), Arg.Any<CancellationToken>());
    }

    protected async Task ThenArgumentExceptionShouldBeThrownAsync(Func<Task> act, string expectedMessagePart)
    {
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage($"*{expectedMessagePart}*");
    }


    // --- HELPERS

    private StateDatabaseFile GetStateDatabaseFileForRepository(AriusFixture fixture, RepositoryVersion version, bool isTemp)
    {
        return StateDatabaseFile.FromRepositoryVersion(fixture.AriusConfiguration, fixture.RemoteRepositoryOptions, version, isTemp);
    }

    public IEnumerable<StateDatabaseFile> GetAllStateDatabaseFilesForRepository(AriusFixture fixture)
    {
        var stateDbFolder = fixture.AriusConfiguration.GetLocalStateDatabaseFolderForRepositoryOptions(fixture.RemoteRepositoryOptions);
        foreach (var fi in stateDbFolder
                     .GetFiles("*.*", SearchOption.AllDirectories)
                     .Where(fi => fi.Name.EndsWith(IStateDatabaseFile.Extension) || fi.Name.EndsWith(IStateDatabaseFile.TempExtension)))
        {
            yield return StateDatabaseFile.FromFullName(stateDbFolder, fi.FullName);
        }
    }

    private static void CreateLocalDatabase(StateDatabaseFile sdbf)
    {
        var optionsBuilder = new DbContextOptionsBuilder<SqliteStateDatabaseContext>();
        optionsBuilder.UseSqlite($"Data Source={sdbf.FullName}");

        using var context = new SqliteStateDatabaseContext(optionsBuilder.Options, _ => {});
        //context.Database.EnsureCreated();
        context.Database.Migrate();
    }
}