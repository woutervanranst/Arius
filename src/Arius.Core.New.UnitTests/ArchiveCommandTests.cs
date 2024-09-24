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
using static Microsoft.EntityFrameworkCore.DbLoggerCategory.Database;
using File = System.IO.File;

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
    protected override AriusFixture GetFixture()
    {
        return FixtureBuilder.Create()
            .WithUniqueContainerName()
            .WithMediatrNotificationStore<ArchiveCommandNotification>()
            //.WithMockedStorageAccountFactory()
            //.WithFakeCryptoService()
            .Build();
    }

    protected override void ConfigureOnceForFixture()
    {
    }

    [Fact]
    public async Task Handle_OneNewFile()
    {
        // Arrange
        var relativeName = "directory/File1.txt";
        var lfs          = GivenLocalFilesystem();
        var fpwh         = GivenSourceFolderHavingFilePair(relativeName, FilePairType.BinaryFileOnly, 100);

        // Act
        await WhenArchiveCommandAsync(fastHash: false, removeLocal: false, tier: StorageTier.Hot, versionName: "v1.0");
        
        // Assert

        // Notifications
        ThenShouldContainMediatorNotification<FilePairFoundNotification>(n => n.FilePair.RelativeNamePlatformNeutral == relativeName);
        ThenShouldContainMediatorNotification<FilePairHashingStartedNotification>(n => n.FilePair.RelativeNamePlatformNeutral == relativeName);
        ThenShouldContainMediatorNotification<FilePairHashingCompletedNotification>(n => n.FilePairWithHash.RelativeNamePlatformNeutral == relativeName);
        ThenShouldContainMediatorNotification<BinaryFileToUpload>(n => n.FilePairWithHash.RelativeNamePlatformNeutral == relativeName);
        ThenShouldContainMediatorNotification<UploadBinaryFileStartedNotification>(n => n.BinaryFile.FullName.Equals(fpwh.BinaryFile.FullName));
        ThenShouldContainMediatorNotification<UploadBinaryFileCompletedNotification>(n => n.BinaryFile.FullName.Equals(fpwh.BinaryFile.FullName) && n.OriginalLength == 100);
        ThenShouldContainMediatorNotification<UploadBinaryFileCompletedNotification>(n => n.BinaryFile.RelativeNamePlatformNeutral == relativeName && n.OriginalLength == 100);
        ThenShouldContainMediatorNotification<CreatedPointerFileNotification>(n => n.PointerFile.BinaryFileRelativeNamePlatformNeutral == relativeName);
        ThenShouldContainMediatorNotification<CreatedPointerFileEntryNotification>(n => n.RelativeNamePlatformSpecific.ToPlatformNeutralPath() == relativeName);
        ThenShouldContainMediatorNotification<NewStateVersionCreatedNotification>(n => n.Version.Name == "v1.0");
        ThenShouldContainMediatorNotification<ArchiveCommandDoneNotification>();

        var stats                = await GetRepositoryStatistics();
        var localStateRepository = await GetLocalStateRepositoryAsync();

        // 1 Binary on the remote
        BlobStorageHelper.BinaryExists(Fixture.RemoteRepositoryOptions, fpwh.Hash).Should().BeTrue();
        stats.BinaryFilesCount.Should().Be(1);
        localStateRepository.BinaryExists(fpwh.Hash).Should().BeTrue();

        // 1 PointerFileEntry
        stats.PointerFilesEntryCount.Should().Be(1);
        localStateRepository.PointerFileEntryExists(fpwh).Should().BeTrue();

        // 1 PointerFile was created
        lfs.PointerFileExists(Fixture, relativeName).Should().BeTrue();

        // Validate SizeMetrics
        stats.Sizes.AllUniqueOriginalSize.Should().Be(100);
        stats.Sizes.AllUniqueArchivedSize.Should().Be(144);
        //stats.Sizes.AllOriginalSize.Should().Be(100);
        //stats.Sizes.AllArchivedSize.Should().Be(144);
        stats.Sizes.ExistingUniqueOriginalSize.Should().Be(100);
        stats.Sizes.ExistingUniqueArchivedSize.Should().Be(144);
        stats.Sizes.ExistingOriginalSize.Should().Be(100);
        stats.Sizes.ExistingArchivedSize.Should().Be(144);
    }

    [Fact]
    public async Task Handle_DuplicateFile()
    {
        // Arrange
        var relativeName1 = "directory/File1.txt";
        var relativeName2 = "directory2/File2.txt";
        var fpwh1         = GivenSourceFolderHavingFilePair(relativeName1, FilePairType.BinaryFileOnly, 100);
        var fpwh2         = GivenSourceFolderHavingCopyOfFilePair(fpwh1, relativeName2);
        var lfs           = GivenLocalFilesystem();

        // Act
        await WhenArchiveCommandAsync(fastHash: false, removeLocal: false, tier: StorageTier.Hot, versionName: "v1.0");

        // Assert
        var stats                = await GetRepositoryStatistics();
        var localStateRepository = await GetLocalStateRepositoryAsync();

        // Only 1 Binary on the remote
        stats.BinaryFilesCount.Should().Be(1);

        // We uploaded 1 binary and awaited another one
        ThenShouldContainMediatorNotification<BinaryFileToUpload>(n => n.FilePairWithHash.Hash == fpwh1.Hash, out var binaryFileToUploadNotification);
        ThenShouldContainMediatorNotification<BinaryFileWaitingForOtherUpload>(n => n.FilePairWithHash.Hash == binaryFileToUploadNotification.FilePairWithHash.Hash);
        ThenShouldContainMediatorNotification<BinaryFileWaitingForOtherUploadDone>(n => n.FilePairWithHash.Hash == binaryFileToUploadNotification.FilePairWithHash.Hash);
        
        // 2 PointerFileEntries
        ThenShouldContainMediatorNotification<CreatedPointerFileEntryNotification>(n => n.RelativeNamePlatformSpecific.ToPlatformNeutralPath() == relativeName1);
        ThenShouldContainMediatorNotification<CreatedPointerFileEntryNotification>(n => n.RelativeNamePlatformSpecific.ToPlatformNeutralPath() == relativeName2);

        stats.PointerFilesEntryCount.Should().Be(2);
        localStateRepository.PointerFileEntryExists(fpwh1).Should().BeTrue();
        localStateRepository.PointerFileEntryExists(fpwh2).Should().BeTrue();

        // 2 PointerFiles are created
        ThenShouldContainMediatorNotification<CreatedPointerFileNotification>(n => n.PointerFile.BinaryFileRelativeNamePlatformNeutral == relativeName1);
        ThenShouldContainMediatorNotification<CreatedPointerFileNotification>(n => n.PointerFile.BinaryFileRelativeNamePlatformNeutral == relativeName2);

        lfs.PointerFileExists(Fixture, relativeName1).Should().BeTrue();
        lfs.PointerFileExists(Fixture, relativeName2).Should().BeTrue();

        fpwh1.PointerFile.Exists.Should().BeTrue();
        fpwh2.PointerFile.Exists.Should().BeTrue();

        // The BinaryFiles exist
        fpwh1.BinaryFile.Exists.Should().BeTrue();
        fpwh2.BinaryFile.Exists.Should().BeTrue();

        // Validate SizeMetrics
        stats.Sizes.AllUniqueOriginalSize.Should().Be(100);
        stats.Sizes.AllUniqueArchivedSize.Should().Be(144);
        //stats.Sizes.AllOriginalSize.Should().Be(100 * 2);
        //stats.Sizes.AllArchivedSize.Should().Be(144 * 2);
        stats.Sizes.ExistingUniqueOriginalSize.Should().Be(100);
        stats.Sizes.ExistingUniqueArchivedSize.Should().Be(144);
        stats.Sizes.ExistingOriginalSize.Should().Be(100 * 2);
        stats.Sizes.ExistingArchivedSize.Should().Be(144 * 2);


        //public record BinaryFileAlreadyUploaded(ArchiveCommand Command, IFilePairWithHash FilePairWithHash) : ArchiveCommandNotification(Command);

        //public record DeletedPointerFileEntryNotification(ArchiveCommand Command, string RelativeNamePlatformSpecific) : ArchiveCommandNotification(Command);
        //public record DeletedBinaryFileNotification(ArchiveCommand Command, IBinaryFileWithHash BinaryFile) : ArchiveCommandNotification(Command);
        //public record UpdatedChunkTierNotification(ArchiveCommand Command, Hash Hash, long ArchivedSize, StorageTier OriginalTier, StorageTier NewTier) : ArchiveCommandNotification(Command);

        //public record NoNewStateVersionCreatedNotification(ArchiveCommand Command) : ArchiveCommandNotification(Command);

        // TODO test with VersionName = null
    }

    [Fact]
    public async Task LatentPointersTest()
    {
        throw new NotImplementedException();
    }
}