using Arius.Core.Domain;
using Arius.Core.Domain.Repositories;
using Arius.Core.Domain.Storage;
using Arius.Core.Domain.Storage.FileSystem;
using Arius.Core.Infrastructure.Storage.Azure;
using Arius.Core.New.Commands.Archive;
using Arius.Core.Tests.Fixtures;
using Azure.Storage.Blobs;
using FluentAssertions;

namespace Arius.Core.Tests;

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
}

public class ArchiveCommandTests : TestBase
{
    protected override AriusFixture GetFixture()
    {
        return new FixtureBuilder()
            .WithRealStorageAccountFactory()
            .WithUniqueContainerName()
            .WithMediatrNotificationStore<ArchiveCommandNotification>()
            //.WithFakeCryptoService()
            .Build();
    }

    protected override void ConfigureOnceForFixture()
    {
    }

    [Fact]
    public async Task Handle_OneNewFile_Success()
    {
        // Arrange
        var relativeName = "directory/File1.txt";
        var fpwh         = GivenSourceFolderHavingFilePair(relativeName, FilePairType.BinaryFileOnly, 100);

        // Act
        await WhenArchiveCommandAsync(fastHash: false, removeLocal: false, tier: StorageTier.Hot, versionName: "v1.0");
        
        // Assert

            // Notifications
        ThenShouldContainMediatorNotification<FilePairFoundNotification>(n => n.FilePair.RelativeNamePlatformNeutral == relativeName);
        ThenShouldContainMediatorNotification<FilePairHashingStartedNotification>(n => n.FilePair.RelativeNamePlatformNeutral == relativeName);
        ThenShouldContainMediatorNotification<FilePairHashingCompletedNotification>(n => n.FilePairWithHash.RelativeNamePlatformNeutral == relativeName);
        ThenShouldContainMediatorNotification<BinaryFileToUploadNotification>(n => n.FilePairWithHash.RelativeNamePlatformNeutral == relativeName);
        ThenShouldContainMediatorNotification<UploadBinaryFileStartedNotification>(n => n.FilePairWithHash.BinaryFile.FullName.Equals(fpwh.BinaryFile.FullName));
        ThenShouldContainMediatorNotification<UploadBinaryFileCompletedNotification>(n => n.FilePairWithHash.BinaryFile.FullName.Equals(fpwh.BinaryFile.FullName) && n.OriginalLength == 100);
        ThenShouldContainMediatorNotification<UploadBinaryFileCompletedNotification>(n => n.FilePairWithHash.BinaryFile.RelativeNamePlatformNeutral == relativeName && n.OriginalLength == 100);
        ThenShouldContainMediatorNotification<CreatedPointerFileNotification>(n => n.PointerFile.BinaryFileRelativeNamePlatformNeutral == relativeName);
        ThenShouldContainMediatorNotification<CreatedPointerFileEntryNotification>(n => n.FilePairWithHash.BinaryFile.RelativeNamePlatformNeutral == relativeName);
        ThenShouldContainMediatorNotification<NewStateVersionCreatedNotification>(n => n.Version.Name == "v1.0");
        ThenShouldContainMediatorNotification<ArchiveCommandDoneNotification>();

        var stats                = await GetRepositoryStatisticsAsync();
        var localStateRepository = await GetLocalStateRepositoryAsync();

            // 1 Binary on the remote
        BlobStorageHelper.BinaryExists(Fixture.RemoteRepositoryOptions, fpwh.Hash).Should().BeTrue();
        stats.BinaryFilesCount.Should().Be(1);
        localStateRepository.BinaryExists(fpwh.Hash).Should().BeTrue();

            // 1 PointerFileEntry
        stats.PointerFilesEntryCount.Should().Be(1);
        localStateRepository.PointerFileEntryExists(fpwh).Should().BeTrue();

            // 1 PointerFile was created
        fpwh.PointerFile.Exists.Should().BeTrue();

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
    public async Task Handle_ArchiveNoChanges()
    {
        // Arrange
        var relativeName = "directory/File1.txt";
        var fpwh         = GivenSourceFolderHavingFilePair(relativeName, FilePairType.BinaryFileOnly, 100);

        await WhenArchiveCommandAsync(fastHash: false, removeLocal: false, tier: StorageTier.Hot, versionName: "v1.0");
        Fixture.ClearMediatorNotifications();

        // Act
        await WhenArchiveCommandAsync(fastHash: false, removeLocal: false, tier: StorageTier.Hot, versionName: "v1.1");

        // Assert
        ThenShouldContainMediatorNotification<FilePairFoundNotification>();
        ThenShouldContainMediatorNotification<FilePairHashingStartedNotification>();
        ThenShouldContainMediatorNotification<FilePairHashingCompletedNotification>();
        ThenShouldNotContainMediatorNotification<BinaryFileToUploadNotification>();
        ThenShouldNotContainMediatorNotification<BinaryFileWaitingForOtherUploadNotification>();
        ThenShouldNotContainMediatorNotification<BinaryFileWaitingForOtherUploadDoneNotification>();
        ThenShouldContainMediatorNotification<BinaryFileAlreadyUploadedNotification>();
        ThenShouldNotContainMediatorNotification<UploadBinaryFileStartedNotification>();
        ThenShouldNotContainMediatorNotification<UploadBinaryFileCompletedNotification>();
        ThenShouldNotContainMediatorNotification<CreatedPointerFileNotification>();
        ThenShouldNotContainMediatorNotification<UpdatedPointerFileNotification>();
        ThenShouldNotContainMediatorNotification<DeletedPointerFileEntryNotification>();
        ThenShouldNotContainMediatorNotification<CreatedPointerFileEntryNotification>();
        ThenShouldNotContainMediatorNotification<UpdatedPointerFileEntryNotification>();
        ThenShouldNotContainMediatorNotification<DeletedBinaryFileNotification>();
        ThenShouldNotContainMediatorNotification<UpdatedChunkTierNotification>();
        ThenShouldNotContainMediatorNotification<NewStateVersionCreatedNotification>();
        ThenShouldContainMediatorNotification<NoNewStateVersionCreatedNotification>();

        var versionNames = GetAllStateDatabaseFilesForRepository().Select(x => x.Version.Name).ToArray();
        versionNames.Should().NotContain("v1.1"); // for v1.1 because it does not contain any changes, there should not be a version
    }

    [Fact]
    public async Task Handle_ArchiveTwice_WithChange()
    {
        // Arrange
        var relativeName = "directory/File1.txt";
        var fpwh         = GivenSourceFolderHavingFilePair(relativeName, FilePairType.BinaryFileOnly, 100);

        await WhenArchiveCommandAsync(fastHash: false, removeLocal: false, tier: StorageTier.Hot, versionName: "v1.0");
        Fixture.ClearMediatorNotifications();

        // Act
        fpwh.BinaryFile.CreationTimeUtc = DateTime.Now.AddDays(-1);

        await WhenArchiveCommandAsync(fastHash: false, removeLocal: false, tier: StorageTier.Hot, versionName: "v1.1");

        // Assert
        ThenShouldContainMediatorNotification<UpdatedPointerFileEntryNotification>(n => n.FilePairWithHash.BinaryFile.FullName.Equals(fpwh.BinaryFile.FullName));
        ThenShouldContainMediatorNotification<NewStateVersionCreatedNotification>(n => n.Version.Name == "v1.1");
    }

    [Fact]
    public async Task Handle_PointerBinaryMismatch()
    {
    }

    [Fact]
    public async Task Handle_AnExceptionHappens()
    {

    }

    [Fact]
    public async Task Handle_DuplicateFile()
    {
        // Arrange
        var relativeName1 = "directory/File1.txt";
        var relativeName2 = "directory2/File2.txt";
        var fpwh1         = GivenSourceFolderHavingFilePair(relativeName1, FilePairType.BinaryFileOnly, 100);
        var fpwh2         = GivenSourceFolderHavingCopyOfFilePair(fpwh1, relativeName2);

        // Act
        await WhenArchiveCommandAsync(fastHash: false, removeLocal: false, tier: StorageTier.Hot, versionName: "v1.0");

        // Assert
        var stats                = await GetRepositoryStatisticsAsync();
        var localStateRepository = await GetLocalStateRepositoryAsync();

        // Only 1 Binary on the remote
        stats.BinaryFilesCount.Should().Be(1);

        // We uploaded 1 binary and awaited another one
        ThenShouldContainMediatorNotification<BinaryFileToUploadNotification>(n => n.FilePairWithHash.Hash == fpwh1.Hash, out var binaryFileToUploadNotification);
        ThenShouldContainMediatorNotification<BinaryFileWaitingForOtherUploadNotification>(n => n.FilePairWithHash.Hash == binaryFileToUploadNotification.FilePairWithHash.Hash);
        ThenShouldContainMediatorNotification<BinaryFileWaitingForOtherUploadDoneNotification>(n => n.FilePairWithHash.Hash == binaryFileToUploadNotification.FilePairWithHash.Hash);
        
        // 2 PointerFileEntries
        ThenShouldContainMediatorNotification<CreatedPointerFileEntryNotification>(n => n.FilePairWithHash.RelativeNamePlatformNeutral == relativeName1);
        ThenShouldContainMediatorNotification<CreatedPointerFileEntryNotification>(n => n.FilePairWithHash.RelativeNamePlatformNeutral == relativeName2);

        stats.PointerFilesEntryCount.Should().Be(2);
        localStateRepository.PointerFileEntryExists(fpwh1).Should().BeTrue();
        localStateRepository.PointerFileEntryExists(fpwh2).Should().BeTrue();

        // 2 PointerFiles are created
        ThenShouldContainMediatorNotification<CreatedPointerFileNotification>(n => n.PointerFile.BinaryFileRelativeNamePlatformNeutral == relativeName1);
        ThenShouldContainMediatorNotification<CreatedPointerFileNotification>(n => n.PointerFile.BinaryFileRelativeNamePlatformNeutral == relativeName2);

        fpwh1.PointerFile.Exists.Should().BeTrue();
        fpwh2.PointerFile.Exists.Should().BeTrue();

        // The BinaryFiles still exist
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
    }

    [Fact]
    public async Task Handle_DeletedUniqueFile()
    {
        // Arrange
        var relativeName        = "directory/File1.txt";
        var deletedRelativeName = "directory2/File2.txt";
        var fpwh1               = GivenSourceFolderHavingFilePair(relativeName,        FilePairType.BinaryFileOnly, 100);
        var deletedFpwh               = GivenSourceFolderHavingFilePair(deletedRelativeName, FilePairType.BinaryFileOnly, 100);

        await WhenArchiveCommandAsync(fastHash: false, removeLocal: false, tier: StorageTier.Hot, versionName: "v1.0");

        deletedFpwh.BinaryFile.Delete();
        deletedFpwh.PointerFile.Delete();

        deletedFpwh.BinaryFile.Exists.Should().BeFalse();
        deletedFpwh.PointerFile.Exists.Should().BeFalse();

        Fixture.ClearMediatorNotifications();
        
        // Act
        await WhenArchiveCommandAsync(fastHash: false, removeLocal: false, tier: StorageTier.Hot, versionName: "v1.1");


        // Assert
        var stats = await GetRepositoryStatisticsAsync();
        var localStateRepository = await GetLocalStateRepositoryAsync();

        // Only 2 Binary on the remote
        stats.BinaryFilesCount.Should().Be(2);

        // We uploaded 1 binary and awaited another one
        ThenShouldContainMediatorNotification<DeletedPointerFileEntryNotification>(n => n.RelativeName == deletedFpwh.RelativeNamePlatformNeutral, out var binaryFileToUploadNotification);

        // 2 PointerFileEntries
        stats.PointerFilesEntryCount.Should().Be(1);
        localStateRepository.PointerFileEntryExists(fpwh1).Should().BeTrue();
        localStateRepository.PointerFileEntryExists(deletedFpwh).Should().BeFalse();

        //// 2 PointerFiles are created
        //ThenShouldContainMediatorNotification<CreatedPointerFileNotification>(n => n.PointerFile.BinaryFileRelativeNamePlatformNeutral == relativeName1);
        //ThenShouldContainMediatorNotification<CreatedPointerFileNotification>(n => n.PointerFile.BinaryFileRelativeNamePlatformNeutral == relativeName2);

        //fpwh1.PointerFile.Exists.Should().BeTrue();
        //fpwh2.PointerFile.Exists.Should().BeTrue();

        //// The BinaryFiles still exist
        //fpwh1.BinaryFile.Exists.Should().BeTrue();
        //fpwh2.BinaryFile.Exists.Should().BeTrue();

        //// Validate SizeMetrics
        //stats.Sizes.AllUniqueOriginalSize.Should().Be(100);
        //stats.Sizes.AllUniqueArchivedSize.Should().Be(144);
        ////stats.Sizes.AllOriginalSize.Should().Be(100 * 2);
        ////stats.Sizes.AllArchivedSize.Should().Be(144 * 2);
        //stats.Sizes.ExistingUniqueOriginalSize.Should().Be(100);
        //stats.Sizes.ExistingUniqueArchivedSize.Should().Be(144);
        //stats.Sizes.ExistingOriginalSize.Should().Be(100 * 2);
        //stats.Sizes.ExistingArchivedSize.Should().Be(144 * 2);






        //public record BinaryFileAlreadyUploadedNotification(ArchiveCommand Command, IFilePairWithHash FilePairWithHash) : ArchiveCommandNotification(Command);

        //public record DeletedBinaryFileNotification(ArchiveCommand Command, IBinaryFileWithHash BinaryFile) : ArchiveCommandNotification(Command);
        //public record UpdatedChunkTierNotification(ArchiveCommand Command, Hash Hash, long ArchivedSize, StorageTier OriginalTier, StorageTier NewTier) : ArchiveCommandNotification(Command);

        //public record NoNewStateVersionCreatedNotification(ArchiveCommand Command) : ArchiveCommandNotification(Command);

        // TODO test with Version = null
    }

    [Fact]
    public async Task Handle_DeletedDuplicateFile()
    {
        // Arrange
        var relativeName        = "directory/File1.txt";
        var deletedRelativeName = "directory2/File2.txt";
        var fpwh1               = GivenSourceFolderHavingFilePair(relativeName, FilePairType.BinaryFileOnly, 100);
        var fpwh2               = GivenSourceFolderHavingCopyOfFilePair(fpwh1, deletedRelativeName);

        await WhenArchiveCommandAsync(fastHash: false, removeLocal: false, tier: StorageTier.Hot, versionName: "v1.0");


    }

    [Fact]
    public async Task LatentPointersTest()
    {
        throw new NotImplementedException();
    }
}