using Arius.Core.Domain;
using Arius.Core.Domain.Services;
using Arius.Core.Domain.Storage;
using Arius.Core.Domain.Storage.FileSystem;
using Arius.Core.Infrastructure.Storage.Azure;
using Arius.Core.New.UnitTests.Fixtures;
using FluentAssertions;

namespace Arius.Core.New.UnitTests;

public class AzureRepostoryTests : TestBase
{
    protected override AriusFixture GetFixture()
    {
        return FixtureBuilder.Create()
            .WithUniqueContainerName()
            .Build();
    }

    protected override void ConfigureOnceForFixture()
    {
        var fp  = GivenSourceFolderHavingFilePair("f", FilePairType.BinaryFileOnly, 100);
        //var hvp = new SHA256Hasher(Fixture.CloudRepositoryOptions);
        //var h   = hvp.GetHashAsync(fp.BinaryFile!).Result;
        bfwh = fp.BinaryFile!; // BinaryFileWithHash.FromBinaryFile(fp.BinaryFile!, new Hash("abc".StringToBytes()));
    }

    private BinaryFileWithHash bfwh;

    [Fact]
    public async Task UploadAsync_HappyPath()
    {
        // Arrange
        var r = (AzureCloudRepository)Fixture.CloudRepository;

        // Act
        var bp = await r.UploadBinaryFileAsync(bfwh, _ => StorageTier.Hot);

        // Assert
        bp.Should().NotBeNull();

        bp.Hash.Should().Be(bfwh.Hash);
        bp.OriginalLength.Should().Be(100);
        bp.ArchivedLength.Should().NotBe(0);
        bp.IncrementalLength.Should().Be(bp.ArchivedLength);
        bp.StorageTier.Should().Be(StorageTier.Hot);

        var b = r.ChunksFolder.GetBlob(bfwh.Hash.Value.BytesToHexString());

        (await b.ExistsAsync()).Should().BeTrue();
        (await b.GetOriginalContentLengthAsync()).Should().Be(100);
        (await b.GetContentLengthAsync()).Should().NotBe(0);
        (await b.GetContentTypeAsync()).Should().Be(ICryptoService.ContentType);
        (await b.GetStorageTierAsync()).Should().Be(StorageTier.Hot);
    }

    [Fact]
    [Trait("Integration", "True")]
    public async Task UploadAsync_WhenAlreadyExists_NotUploadedAgain()
    {
        // Arrange
            // Upload the file a first time
        var r = (AzureCloudRepository)Fixture.CloudRepository;
        var bp1 = await r.UploadBinaryFileAsync(bfwh, _ => StorageTier.Hot);

            // Add a marker
        var b = r.ChunksFolder.GetBlob(bfwh.Hash.Value.BytesToHexString());
        await b.UpsertMetadata("Test", "Test");

        // Act
        var bp2 = await r.UploadBinaryFileAsync(bfwh, _ => StorageTier.Hot);

        // Assert
            // The marker is still present
        (await b.GetMetadataAsync()).Should().ContainKey("Test");

            // The returned values are the same
        bp2.Should().BeEquivalentTo(bp1);
    }

    [Fact]
    [Trait("Integration", "True")]
    public async Task UploadAsync_WhenAlreadyExistsButCorrupt_UploadedAgain()
    {
        // Arrange
            // Upload the file a first time
        var r   = (AzureCloudRepository)Fixture.CloudRepository;
        var bp1 = await r.UploadBinaryFileAsync(bfwh, _ => StorageTier.Hot);

            // Add a marker
        var b = r.ChunksFolder.GetBlob(bfwh.Hash.Value.BytesToHexString());
        await b.UpsertMetadata("Test", "Test");

            // 'Corrupt' the file
        await b.SetContentTypeAsync("");

        // Act
        var bp2 = await r.UploadBinaryFileAsync(bfwh, _ => StorageTier.Hot);

        // Assert
            // The marker is gone
        b.Refresh();
        (await b.GetMetadataAsync()).Should().NotContainKey("Test");

        // The returned values are the same
        bp2.Should().BeEquivalentTo(bp1);
    }
}