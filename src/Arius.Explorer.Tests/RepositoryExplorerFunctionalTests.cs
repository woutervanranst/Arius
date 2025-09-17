using System;
using System.Linq;
using System.Threading;
using FlaUI.Core.AutomationElements;
using NUnit.Framework;
using Shouldly;

namespace Arius.Explorer.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
[NonParallelizable]
[Platform(Include = "Win")]
public class RepositoryExplorerFunctionalTests : ExplorerUiTestBase
{
    [Test]
    public void RepositoryExplorer_ShouldDisplayDefaultStatus()
    {
        MainWindow.Title.ShouldBe("Arius Explorer - Repository Browser");

        var selectedStatus = MainWindow
            .FindFirstDescendant(cf => cf.ByAutomationId("SelectedItemsStatus"))
            .ShouldNotBeNull();
        selectedStatus.Name.ShouldBe("No items selected");

        var archiveStatus = MainWindow
            .FindFirstDescendant(cf => cf.ByAutomationId("ArchiveStatisticsStatus"))
            .ShouldNotBeNull();
        archiveStatus.Name.ShouldBe("Repository not loaded");
    }

    [Test]
    public void RepositoryExplorer_ShouldExposeSampleRepositoryStructure()
    {
        var tree = MainWindow
            .FindFirstDescendant(cf => cf.ByAutomationId("RepositoryTree"))
            .ShouldNotBeNull("Repository tree was not found.")
            .AsTree();

        var roots = tree.Items;
        roots.Length.ShouldBeGreaterThanOrEqualTo(1, "At least one root node is expected.");
        var root = roots[0];
        root.Text.ShouldBe("Sample Repository");

        root.Expand();
        WaitForCondition(() => root.Items.Length == 3, TimeSpan.FromSeconds(2), "child folders to load");

        var childNames = root.Items.Select(item => item.Text).ToArray();
        childNames.ShouldBeEquivalentTo(new[] { "Documents", "Images", "Videos" });
    }

}
