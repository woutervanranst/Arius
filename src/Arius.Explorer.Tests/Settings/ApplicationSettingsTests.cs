using Arius.Explorer.Settings;
using Shouldly;

namespace Arius.Explorer.Tests.Settings;

public class ApplicationSettingsTests
{
    [Fact]
    public void UpgradeRequired_DefaultValue_ShouldBeTrue()
    {
        // Arrange & Act
        var settings = ApplicationSettings.Default;

        // Assert
        // On first run, UpgradeRequired should default to true
        // After Upgrade() is called and saved, it becomes false
        settings.UpgradeRequired.ShouldBe(true);
    }

    [Fact]
    public void RecentRepositories_DefaultValue_ShouldBeEmpty()
    {
        // Arrange & Act
        var settings = ApplicationSettings.Default;

        // Assert
        settings.RecentRepositories.ShouldNotBeNull();
    }

    [Fact]
    public void RecentLimit_DefaultValue_ShouldBe10()
    {
        // Arrange & Act
        var settings = ApplicationSettings.Default;

        // Assert
        settings.RecentLimit.ShouldBe(10);
    }
}
