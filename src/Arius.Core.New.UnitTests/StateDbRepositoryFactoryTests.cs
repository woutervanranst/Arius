using Arius.Core.New.UnitTests.Fixtures;

namespace Arius.Core.New.UnitTests;

public sealed class StateDbRepositoryFactoryTests : TestBase
{
    protected override IAriusFixture ConfigureFixture()
    {
        return new MockAriusFixture();
    }

    [Fact]
    public async Task CreateAsync_WhenNewRepository_NewLocalDatabaseInitializedNotDownloaded()
    {
        // Arrange
        GivenLocalFilesystem();
        GivenAzureRepositoryWithNoVersions();

        var startTime = DateTime.UtcNow.TruncateToSeconds();

        // Act
        var result = await WhenCreatingStateDb();

        // Assert
        var endTime = DateTime.UtcNow.TruncateToSeconds();

        ThenStateDbVersionShouldBeBetween(result, startTime, endTime);
        ThenLocalStateDbShouldExist(result);
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
        var result = await WhenCreatingStateDb();

        // Assert
        ThenStateDbVersionShouldBe(result, "v2.0");
        ThenLocalStateDbShouldExist(result);
        ThenDownloadShouldHaveBeenCalled();
    }

    [Fact]
    public async Task CreateAsync_WhenLatestVersionCached_ShouldNotDownloadLatestVersion()
    {
        // Arrange
        GivenLocalFilesystemWithVersions("v1.0", "v1.1", "v2.0");

        // Act
        var result = await WhenCreatingStateDb();

        // Assert
        ThenStateDbVersionShouldBe(result, "v2.0");
        ThenDownloadShouldNotHaveBeenCalled();
    }

    [Fact]
    public async Task CreateAsync_WhenSpecificVersionExistsLocally_ShouldNotDownloadSpecificVersion()
    {
        // Arrange
        GivenLocalFilesystemWithVersions("v1.0", "v1.1", "v2.0");

        // Act
        var result = await WhenCreatingStateDb("v1.1");

        // Assert
        ThenStateDbVersionShouldBe(result, "v1.1");
        ThenLocalStateDbShouldExist(result);
        ThenDownloadShouldNotHaveBeenCalled();
    }

    [Fact]
    public async Task CreateAsync_WhenVersionSpecifiedButNotCached_ShouldDownloadSpecificVersion()
    {
        // Arrange
        GivenLocalFilesystem();
        GivenAzureRepositoryWithVersions("v1.0", "v1.1", "v2.0");

        // Act
        var result = await WhenCreatingStateDb("v1.1");

        // Assert
        ThenStateDbVersionShouldBe(result, "v1.1");
        ThenLocalStateDbShouldExist(result);
        ThenDownloadShouldHaveBeenCalled();
    }

    [Fact]
    public async Task CreateAsync_WhenVersionSpecifiedButCached_ShouldNotDownloadSpecificVersion()
    {
        // Arrange
        GivenLocalFilesystemWithVersions("v1.0", "v1.1", "v2.0");

        // Act
        var result = await WhenCreatingStateDb("v1.1");

        // Assert
        ThenStateDbVersionShouldBe(result, "v1.1");
        ThenDownloadShouldNotHaveBeenCalled();
    }

    [Fact]
    public async Task CreateAsync_WhenSpecificVersionDoesNotExist_ShouldThrowArgumentException()
    {
        // Arrange
        GivenLocalFilesystem();
        GivenAzureRepositoryWithVersions("v1.0", "v2.0");

        // Act
        Func<Task> act = async () => await WhenCreatingStateDb("v3.0");

        // Assert
        await ThenArgumentExceptionShouldBeThrownAsync(act, "The requested version was not found*");
    }
}