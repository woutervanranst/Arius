using Arius.Core.Domain.Repositories;
using Arius.Core.Domain.Storage;
using Arius.Core.Infrastructure.Repositories;
using Arius.Core.New.UnitTests.Fixtures;
using Azure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using WouterVanRanst.Utils;

namespace Arius.Core.New.UnitTests;

public abstract class TestBase
{
    protected IAriusFixture Fixture { get; }

    protected TestBase()
    {
        Fixture = ConfigureFixture();
    }

    protected abstract IAriusFixture ConfigureFixture();

    protected void GivenLocalFilesystem()
    {
        // No need to initialize anything
    }

    protected void GivenLocalFilesystemWithVersions(params string[] versionNames)
    {
        var repository = Fixture.StorageAccountFactory.GetRepository(Fixture.RepositoryOptions);
        var versions   = versionNames.Select(name => new RepositoryVersion { Name = name }).ToArray();
        repository.GetRepositoryVersions().Returns(versions.ToAsyncEnumerable());

        foreach (var versionName in versionNames)
        {
            var version    = new RepositoryVersion { Name = versionName };
            var dbFullName = GetLocalStateDbForRepositoryFullName(Fixture, Fixture.RepositoryOptions, version);

            CreateLocalDatabase(dbFullName);
        }

        void CreateLocalDatabase(string dbFullName)
        {
            var optionsBuilder = new DbContextOptionsBuilder<SqliteStateDbContext>();
            optionsBuilder.UseSqlite($"Data Source={dbFullName}");

            using var context = new SqliteStateDbContext(optionsBuilder.Options);
            context.Database.EnsureCreated();
            context.Database.Migrate();
        }
    }

    protected void GivenAzureRepositoryWithNoVersions()
    {
        var repository = Fixture.StorageAccountFactory.GetRepository(Fixture.RepositoryOptions);
        repository.GetRepositoryVersions().Returns(AsyncEnumerable.Empty<RepositoryVersion>());
    }

    protected void GivenAzureRepositoryWithVersions(params string[] versionNames)
    {
        var repository = Fixture.StorageAccountFactory.GetRepository(Fixture.RepositoryOptions);
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

        repository.DownloadAsync(Arg.Any<IBlob>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(info =>
            {
                var blob = info.Arg<IBlob>();
                if (blob == null)
                {
                    return Task.FromException(new RequestFailedException(404, "Blob not found", "BlobNotFound", null));
                }
                return Task.CompletedTask;
            });
    }

    protected async Task<IStateDbRepository> WhenCreatingStateDb(string versionName = null)
    {
        var factory           = Fixture.StateDbRepositoryFactory;
        var repositoryOptions = Fixture.RepositoryOptions;
        var version           = versionName != null ? new RepositoryVersion { Name = versionName } : null;
        return await factory.CreateAsync(repositoryOptions, version);
    }

    protected void ThenStateDbVersionShouldBe(IStateDbRepository stateDbRepository, string expectedVersion)
    {
        stateDbRepository.Version.Name.Should().Be(expectedVersion);
    }

    protected void ThenStateDbVersionShouldBeBetween(IStateDbRepository stateDbRepository, DateTime startTime, DateTime endTime)
    {
        DateTime.Parse(stateDbRepository.Version.Name)
            .Should()
            .BeOnOrAfter(startTime).And.BeOnOrBefore(endTime);
    }

    protected void ThenLocalStateDbShouldExist(IStateDbRepository stateDbRepository)
    {
        File.Exists(GetLocalStateDbForRepositoryFullName(Fixture, Fixture.RepositoryOptions, stateDbRepository.Version))
            .Should().BeTrue();
    }

    protected void ThenStateDbShouldBeEmpty(IStateDbRepository stateDbRepository)
    {
        stateDbRepository.GetPointerFileEntries().CountAsync().Result.Should().Be(0);
        stateDbRepository.GetBinaryEntries().CountAsync().Result.Should().Be(0);
    }

    protected void ThenDownloadShouldNotHaveBeenCalled()
    {
        var repository = Fixture.StorageAccountFactory.GetRepository(Fixture.RepositoryOptions);
        repository.DidNotReceive().DownloadAsync(Arg.Any<IBlob>(), Arg.Any<string>(), Arg.Any<string>());
    }

    protected void ThenDownloadShouldHaveBeenCalled()
    {
        var repository = Fixture.StorageAccountFactory.GetRepository(Fixture.RepositoryOptions);
        repository.Received(1).DownloadAsync(Arg.Any<IBlob>(), Arg.Any<string>(), Arg.Any<string>());
    }

    protected async Task ThenArgumentExceptionShouldBeThrownAsync(Func<Task> act, string expectedMessagePart)
    {
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage($"*{expectedMessagePart}*");
    }

    private string GetLocalStateDbForRepositoryFullName(IAriusFixture fixture, RepositoryOptions repositoryOptions, RepositoryVersion version)
    {
        return fixture.AriusConfiguration
            .GetLocalStateDbFolderForRepository(repositoryOptions)
            .GetFullName(version.GetFileSystemName());
    }
}