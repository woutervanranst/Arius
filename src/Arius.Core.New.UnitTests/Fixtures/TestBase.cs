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
        var repository = Fixture.Repository;
        var versions   = versionNames.Select(name => new RepositoryVersion { Name = name }).ToArray();
        repository.GetRepositoryVersions().Returns(versions.ToAsyncEnumerable());

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
        var repository = Fixture.Repository;
        repository.GetRepositoryVersions().Returns(AsyncEnumerable.Empty<RepositoryVersion>());
    }

    protected void GivenAzureRepositoryWithVersions(string[] versionNames)
    {
        var repository = Fixture.Repository;
        var versions   = versionNames.Select(name => new RepositoryVersion { Name = name }).ToArray();
        repository.GetRepositoryVersions().Returns(versions.ToAsyncEnumerable());

        // Set up the repository to throw an exception for versions not in the list
        repository.GetRepositoryVersionBlob(Arg.Any<RepositoryVersion>())
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

    internal FilePair GivenSourceFolderHavingRandomFile(string binaryFileRelativeName, long sizeInBytes, FileAttributes attributes = FileAttributes.Normal)
    {
        var fileFullName = Fixture.TestRunSourceFolder.GetFileFullName(binaryFileRelativeName);

        FileUtils.CreateRandomFile(fileFullName, sizeInBytes);
        SetAttributes(attributes, fileFullName);

        return new(null, BinaryFile.FromFullName(null, fileFullName));


        static void SetAttributes(FileAttributes attributes, string filePath)
        {
            File.SetAttributes(filePath, attributes);

            var actualAtts = File.GetAttributes(filePath);
            if (actualAtts != attributes)
                throw new InvalidOperationException($"Could not set attributes for {filePath}");
        }
    }

    internal FilePairWithHash GivenSourceFolderHavingRandomFileWithPointerFile(string binaryFileRelativeName, long sizeInBytes, FileAttributes attributes = FileAttributes.Normal)
    {
        GivenSourceFolderHavingRandomFile(binaryFileRelativeName, sizeInBytes, attributes);

        var bf   = BinaryFile.FromRelativeName(Fixture.TestRunSourceFolder, binaryFileRelativeName);
        var h    = Fixture.HashValueProvider.GetHashAsync(bf).Result;
        var bfwh = BinaryFileWithHash.FromBinaryFile(bf, h);
        var pfwh = PointerFileWithHash.Create(bfwh);

        return new(pfwh, bfwh);
    }

    internal FilePairWithHash GivenSourceFolderHavingPointerFile(string pointerFileRelativeName, Hash h)
    {
        var pfwh = PointerFileWithHash.Create(Fixture.TestRunSourceFolder, pointerFileRelativeName, h, DateTime.UtcNow, DateTime.UtcNow);

        if (!pfwh.IsPointerFile) // check the extension
            throw new InvalidOperationException("This is not a pointer file");

        return new (pfwh, null);
    }


    // --- WHEN

    protected async Task<IStateDbRepository> WhenStateDbRepositoryFactoryCreateAsync(string? versionName = null)
    {
        var factory           = Fixture.StateDbRepositoryFactory;
        var repositoryOptions = Fixture.RepositoryOptions;
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

    protected void ThenStateDbVersionShouldBe(IStateDbRepository repository, string expectedVersion)
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

    protected void ThenStateDbShouldBeEmpty(IStateDbRepository stateDbRepository)
    {
        stateDbRepository.CountPointerFileEntries().Should().Be(0);
        stateDbRepository.CountBinaryProperties().Should().Be(0);
    }

    protected void ThenDownloadShouldNotHaveBeenCalled()
    {
        var repository = Fixture.StorageAccountFactory.GetRepository(Fixture.RepositoryOptions);
        repository.DidNotReceive().DownloadAsync(Arg.Any<IBlob>(), Arg.Any<IFile>(), Arg.Any<CancellationToken>());
    }

    protected void ThenDownloadShouldHaveBeenCalled()
    {
        var repository = Fixture.Repository;
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
        var stateDbFolder = fixture.AriusConfiguration.GetLocalStateDbFolderForRepository(fixture.RepositoryOptions);
        var x = StateDatabaseFile.FromRepositoryVersion(stateDbFolder, version, isTemp);
        return x;
    }

    public IEnumerable<StateDatabaseFile> GetAllStateDatabaseFilesForRepository(AriusFixture fixture)
    {
        var stateDbFolder = fixture.AriusConfiguration.GetLocalStateDbFolderForRepository(fixture.RepositoryOptions);
        foreach (var fi in stateDbFolder
                     .GetFiles("*.*", SearchOption.AllDirectories)
                     .Where(fi => fi.Name.EndsWith(StateDatabaseFile.Extension) || fi.Name.EndsWith(StateDatabaseFile.TempExtension)))
        {
            yield return StateDatabaseFile.FromFullName(stateDbFolder, fi.FullName);
        }
    }

    private static void CreateLocalDatabase(StateDatabaseFile sdbf)
    {
        var optionsBuilder = new DbContextOptionsBuilder<SqliteStateDbContext>();
        optionsBuilder.UseSqlite($"Data Source={sdbf.FullName}");

        using var context = new SqliteStateDbContext(optionsBuilder.Options);
        //context.Database.EnsureCreated();
        context.Database.Migrate();
    }
}