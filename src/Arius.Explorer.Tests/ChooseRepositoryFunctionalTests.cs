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
public class ChooseRepositoryFunctionalTests : ExplorerUiTestBase
{
    private Window? _dialog;

    [SetUp]
    public void OpenChooseRepository()
    {
        _dialog = OpenChooseRepositoryDialog();
    }

    [TearDown]
    public void CloseChooseRepository()
    {
        if (_dialog is not null)
        {
            _dialog.Close();
            WaitForCondition(() => MainWindow.ModalWindows.Length == 0, TimeSpan.FromSeconds(5), "Choose Repository dialog to close");
        }

        _dialog = null;
    }

    [Test]
    //[Ignore("Needs rework")]
    public void ChooseRepository_ShouldDisplayDefaultValues()
    {
        var dialog = _dialog.ShouldNotBeNull("Choose Repository dialog was not opened.");
        dialog.Title.ShouldBe("Choose Repository");

        var localPath = dialog
            .FindFirstDescendant(cf => cf.ByAutomationId("LocalPathTextBox"))
            .ShouldNotBeNull()
            .AsTextBox();
        localPath.Text.ShouldBe(@"C:\SampleRepository");

        var accountName = dialog
            .FindFirstDescendant(cf => cf.ByAutomationId("AccountNameTextBox"))
            .ShouldNotBeNull()
            .AsTextBox();
        accountName.Text.ShouldBe("samplestorageaccount");

        var accountKey = dialog
            .FindFirstDescendant(cf => cf.ByAutomationId("AccountKeyTextBox"))
            .ShouldNotBeNull()
            .AsTextBox();
        accountKey.Text.ShouldBe(string.Empty);

        dialog
            .FindFirstDescendant(cf => cf.ByAutomationId("PassphrasePasswordBox"))
            .ShouldNotBeNull();

        var openButton = dialog
            .FindFirstDescendant(cf => cf.ByAutomationId("OpenRepositoryButton"))
            .ShouldNotBeNull()
            .AsButton();
        openButton.IsEnabled.ShouldBeTrue();
    }

    [Test]
    public void ChooseRepository_ShouldAllowChangingContainer()
    {
        var dialog = _dialog.ShouldNotBeNull("Choose Repository dialog was not opened.");

        var containerCombo = dialog
            .FindFirstDescendant(cf => cf.ByAutomationId("ContainerComboBox"))
            .ShouldNotBeNull()
            .AsComboBox();

        containerCombo.Expand();
        var containerNames = containerCombo.Items.Select(item => item.Text).ToArray();
        containerNames.ShouldBeEquivalentTo(new[] { "container1", "container2", "backups", "archives" });

        var target = containerCombo.Items.First(item => item.Text == "archives");
        target.Select();
        WaitForCondition(() => containerCombo.SelectedItem?.Text == "archives", TimeSpan.FromSeconds(2), "container selection to update");

        var selected = containerCombo.SelectedItem.ShouldNotBeNull();
        selected.Text.ShouldBe("archives");
    }

    [Test]
    [Ignore("Not implemented yet")]
    public void ChooseRepository_ShouldReactToBrowseCommand()
    {
        var dialog = _dialog.ShouldNotBeNull("Choose Repository dialog was not opened.");

        var browseButton = dialog
            .FindFirstDescendant(cf => cf.ByAutomationId("BrowseLocalPathButton"))
            .ShouldNotBeNull()
            .AsButton();
        browseButton.Invoke();

        var localPath = dialog
            .FindFirstDescendant(cf => cf.ByAutomationId("LocalPathTextBox"))
            .ShouldNotBeNull()
            .AsTextBox();
        WaitForCondition(() => localPath.Text == @"C:\\Users\\Sample\\Documents\\MyRepository", TimeSpan.FromSeconds(2), "local path to update after browse");

        localPath.Text.ShouldBe(@"C:\\Users\\Sample\\Documents\\MyRepository");
    }
}
