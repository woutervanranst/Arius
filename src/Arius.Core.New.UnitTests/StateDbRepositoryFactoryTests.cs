using Arius.Core.New.UnitTests.Extensions;
using Arius.Core.New.UnitTests.Fixtures;

namespace Arius.Core.New.UnitTests;

public sealed class StateDbRepositoryFactoryTests : TestBase
{
    protected override AriusFixture GetFixture()
    {
        return FixtureBuilder.Create()
            .WithMockedStorageAccountFactory()
            .WithFakeCryptoService()
            .WithContainerName("bla")
            .Build();
    }

    protected override void ConfigureOnceForFixture()
    {
    }

    [Fact]
    public async Task CreateAsync_WhenNewRepository_NewLocalDatabaseInitializedNotDownloaded()
    {
        // Arrange
        GivenLocalFilesystem();
        GivenAzureRepositoryWithNoVersions();

        var startTime = DateTime.UtcNow.TruncateToSeconds();

        // Act
        var result = await WhenStateDbRepositoryFactoryCreateAsync();

        // Assert
        var endTime = DateTime.UtcNow.TruncateToSeconds();

        ThenStateDbVersionShouldBeBetween(result.Version, startTime, endTime);
        ThenLocalStateDbsShouldExist(tempVersionCount: 1, cachedVersionCount: 0);
        ThenStateDbShouldBeEmpty(result);
        ThenDownloadShouldNotHaveBeenCalled();
    }

    [Fact]
    public async Task CreateAsync_WhenNoVersionSpecifiedAndLatestVersionNotCached_ShouldDownloadLatestVersion()
    {
        // Arrange
        GivenLocalFilesystem();
        GivenAzureRepositoryWithVersions("v1.0", "v1.1", "v2.0");

        // Act
        var result = await WhenStateDbRepositoryFactoryCreateAsync();

        // Assert
        ThenStateDbVersionShouldBe(result.Version, "v2.0");
        ThenLocalStateDbsShouldExist(tempVersions: ["v2.0"], cachedVersions: ["v2.0"], distinctCount: 1);
        ThenDownloadShouldHaveBeenCalled();
    }

    [Fact]
    public async Task CreateAsync_WhenLatestVersionCached_ShouldNotDownloadLatestVersion()
    {
        // Arrange
        GivenLocalFilesystemWithVersions("v1.0", "v1.1", "v2.0");

        // Act
        var result = await WhenStateDbRepositoryFactoryCreateAsync();

        // Assert
        ThenStateDbVersionShouldBe(result.Version, "v2.0");
        ThenLocalStateDbsShouldExist(["v2.0"], ["v1.0", "v1.1", "v2.0"], tempVersionCount: 1, cachedVersionCount: 3, distinctCount: 3);
        ThenDownloadShouldNotHaveBeenCalled();
    }

    [Fact]
    public async Task CreateAsync_WhenSpecificVersionExistsLocally_ShouldNotDownloadSpecificVersion()
    {
        // Arrange
        GivenLocalFilesystemWithVersions("v1.0", "v1.1", "v2.0");

        // Act
        var result = await WhenStateDbRepositoryFactoryCreateAsync("v1.1");

        // Assert
        ThenStateDbVersionShouldBe(result.Version, "v1.1");
        ThenLocalStateDbsShouldExist(["v1.1"], ["v1.0", "v1.1", "v2.0"], tempVersionCount: 1, cachedVersionCount: 3, distinctCount: 3);
        ThenDownloadShouldNotHaveBeenCalled();
    }

    [Fact]
    public async Task CreateAsync_WhenVersionSpecifiedButNotCached_ShouldDownloadSpecificVersion()
    {
        // Arrange
        GivenLocalFilesystem();
        GivenAzureRepositoryWithVersions("v1.0", "v1.1", "v2.0");

        // Act
        var result = await WhenStateDbRepositoryFactoryCreateAsync("v1.1");

        // Assert
        ThenStateDbVersionShouldBe(result.Version, "v1.1");
        ThenLocalStateDbsShouldExist(["v1.1"], ["v1.1"], tempVersionCount: 1, cachedVersionCount: 1, distinctCount: 1);
        ThenDownloadShouldHaveBeenCalled();
    }

    [Fact]
    public async Task CreateAsync_WhenVersionSpecifiedButCached_ShouldNotDownloadSpecificVersion()
    {
        // Arrange
        GivenLocalFilesystemWithVersions("v1.0", "v1.1", "v2.0");

        // Act
        var result = await WhenStateDbRepositoryFactoryCreateAsync("v1.1");

        // Assert
        ThenStateDbVersionShouldBe(result.Version, "v1.1");
        ThenLocalStateDbsShouldExist(["v1.1"], ["v1.0", "v1.1", "v2.0"], tempVersionCount: 1, cachedVersionCount: 3, distinctCount: 3);
        ThenDownloadShouldNotHaveBeenCalled();
    }

    [Fact]
    public async Task CreateAsync_WhenSpecificVersionDoesNotExist_ShouldThrowArgumentException()
    {
        // Arrange
        GivenLocalFilesystem();
        GivenAzureRepositoryWithVersions("v1.0", "v2.0");

        // Act
        Func<Task> act = async () => await WhenStateDbRepositoryFactoryCreateAsync("v3.0");

        // Assert
        await ThenArgumentExceptionShouldBeThrownAsync(act, "The requested version was not found*");
    }
}