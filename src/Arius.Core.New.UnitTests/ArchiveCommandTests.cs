using Arius.Core.Domain;
using Arius.Core.Domain.Extensions;
using Arius.Core.Domain.Repositories;
using Arius.Core.Domain.Storage;
using Arius.Core.Domain.Storage.FileSystem;
using Arius.Core.Infrastructure.Storage.Azure;
using Arius.Core.Infrastructure.Storage.LocalFileSystem;
using Arius.Core.New.Commands.Archive;
using Arius.Core.New.Queries.RepositoryStatistics;
using Arius.Core.New.UnitTests.Fixtures;
using Azure.Storage.Blobs;
using FluentAssertions;

namespace Arius.Core.New.UnitTests;

internal static class BlobStorageHelper
{
    public static bool BinaryExists(RemoteRepositoryOptions options, Hash h)
    {
        var blobName = $"{AzureRemoteRepository.CHUNKS_FOLDER_NAME}/{h}";
        return BlobExists(options, blobName);
    }
    public static bool BlobExists(RemoteRepositoryOptions options, string blobName)
    {
        var blobServiceClient = new BlobServiceClient(new Uri($"https://{options.AccountName}.blob.core.windows.net"),
            new Azure.Storage.StorageSharedKeyCredential(options.AccountName, options.AccountKey));

        var containerClient = blobServiceClient.GetBlobContainerClient(options.ContainerName);
        var blobClient      = containerClient.GetBlobClient(blobName);

        return blobClient.Exists();
    }

    public static bool PointerFileEntryExists(this ILocalStateRepository localStateRepository, IFilePairWithHash fpwh) 
        => localStateRepository.GetPointerFileEntries().SingleOrDefault(pfe => pfe.Hash == fpwh.Hash && pfe.RelativeName.StartsWith(fpwh.RelativeName)) is not null;

    public static bool PointerFileExists(this IFileSystem fileSystem, AriusFixture fixture, string relativeName)
        => fileSystem.EnumerateFilePairs(fixture.TestRunSourceFolder).Any(fp => fp.RelativeNamePlatformNeutral == relativeName);
}

public class ArchiveCommandTests : TestBase
{
    private FilePairWithHash fpwh;
    private IFileSystem      lfs;
    private string           relativeName;

    protected override AriusFixture GetFixture()
    {
        return FixtureBuilder.Create()
            .WithUniqueContainerName()
            //.WithMockedStorageAccountFactory()
            //.WithFakeCryptoService()
            .Build();
    }

    protected override void ConfigureOnceForFixture()
    {
        relativeName = "directory/File1.txt";
        lfs          = GivenLocalFilesystem();
        fpwh         = GivenSourceFolderHavingFilePair(relativeName, FilePairType.BinaryFileOnly, 100);
    }

    [Fact]
    public async Task Handle()
    {
        // Arrange
        var q  = new RepositoryStatisticsQuery { RemoteRepository = Fixture.RemoteRepositoryOptions };
        var s0 = await WhenMediatorRequest(q);

        var c = new ArchiveCommand
        {
            RemoteRepositoryOptions = Fixture.RemoteRepositoryOptions,
            FastHash                = false,
            RemoveLocal             = false,
            Tier                    = StorageTier.Hot,
            LocalRoot               = Fixture.TestRunSourceFolder,
            VersionName             = new RepositoryVersion { Name = "v1.0" }
        };

        // Act
        await WhenMediatorRequest(c);
        

        // Assert
        var s1                   = await WhenMediatorRequest(q);
        var localStateRepository = await GetLocalStateRepositoryAsync();

        // 1 additional Binary
        BlobStorageHelper.BinaryExists(Fixture.RemoteRepositoryOptions, fpwh.Hash).Should().BeTrue();
        s1.BinaryFilesCount.Should().Be(1);
        localStateRepository.BinaryExists(fpwh.Hash).Should().BeTrue();

        // 1 additional PointerFileEntry
        s1.PointerFilesEntryCount.Should().Be(1);
        localStateRepository.PointerFileEntryExists(fpwh).Should().BeTrue();

        // 1 matching PointerFile
        lfs.PointerFileExists(Fixture, relativeName).Should().BeTrue();
        
        ;
        
        

        // 1 additional Binary

        s1.ArchiveSize.Should().BeGreaterThan(0);

    }

    [Fact]
    public async Task LatentPointersTest()
    {
        throw new NotImplementedException();
    }
}