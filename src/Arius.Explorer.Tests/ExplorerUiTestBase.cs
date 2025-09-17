using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Tools;
using FlaUI.UIA3;
using NUnit.Framework;
using Shouldly;

namespace Arius.Explorer.Tests;

public abstract class ExplorerUiTestBase
{
    private Application? _application;
    private UIA3Automation? _automation;
    private Window? _mainWindow;

    protected Application Application => _application ?? throw new InvalidOperationException("The Arius Explorer process has not been started.");

    protected UIA3Automation Automation => _automation ?? throw new InvalidOperationException("UI Automation infrastructure has not been initialised.");

    protected Window MainWindow => _mainWindow ?? throw new InvalidOperationException("The Arius Explorer main window is not available.");

    [OneTimeSetUp]
    public void BaseOneTimeSetUp()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Ignore("Arius Explorer UI automation tests can only run on Windows.");
        }

        var executablePath = ResolveExecutablePath();

        var startInfo = new ProcessStartInfo(executablePath)
        {
            WorkingDirectory = Path.GetDirectoryName(executablePath) ?? Environment.CurrentDirectory,
            UseShellExecute = false
        };

        _application = Application.Launch(startInfo);
        _automation = new UIA3Automation();

        _mainWindow = WaitForElement(() => _application.GetMainWindow(_automation), TimeSpan.FromSeconds(15), "the Arius Explorer main window");
        WaitForCondition(() => _mainWindow.Title.Contains("Repository Browser", StringComparison.OrdinalIgnoreCase), TimeSpan.FromSeconds(5), "the repository browser title to appear");

        _mainWindow.WaitUntilClickable(TimeSpan.FromSeconds(10));
        _mainWindow.SetForeground();
    }

    [SetUp]
    public void BaseSetUp()
    {
        var mainWindow = _mainWindow.ShouldNotBeNull("Main window was not initialised.");

        mainWindow.WaitUntilClickable(TimeSpan.FromSeconds(5));
        mainWindow.SetForeground();
    }

    [TearDown]
    public void BaseTearDown()
    {
        if (_mainWindow is null)
        {
            return;
        }

        var modals = _mainWindow.ModalWindows;
        if (modals.Length == 0)
        {
            return;
        }

        foreach (var modal in modals)
        {
            modal.Close();
        }

        WaitForCondition(() => _mainWindow.ModalWindows.Length == 0, TimeSpan.FromSeconds(5), "modal windows to close");
    }

    [OneTimeTearDown]
    public void BaseOneTimeTearDown()
    {
        try
        {
            if (_mainWindow != null)
            {
                foreach (var modal in _mainWindow.ModalWindows)
                {
                    modal.Close();
                }
            }

            if (_application is { HasExited: false })
            {
                _application.Close();
                _application.WaitForExit(TimeSpan.FromSeconds(10));
            }
        }
        finally
        {
            _automation?.Dispose();
            _application?.Dispose();
        }
    }

    protected T WaitForElement<T>(Func<T?> getter, TimeSpan timeout, string description) where T : class
    {
        var result = Retry.WhileNull(getter, timeout, TimeSpan.FromMilliseconds(200));
        if (result.Success && result.Result is not null)
        {
            return result.Result;
        }

        throw new TimeoutException($"Timed out waiting for {description} within {timeout}.");
    }

    protected void WaitForCondition(Func<bool> condition, TimeSpan timeout, string description)
    {
        var result = Retry.WhileFalse(condition, timeout, TimeSpan.FromMilliseconds(200));
        if (!result.Success)
        {
            throw new TimeoutException($"Timed out waiting for {description} within {timeout}.");
        }
    }

    protected Window OpenChooseRepositoryDialog()
    {
        var fileMenuElement = MainWindow
            .FindFirstDescendant(cf => cf.ByAutomationId("FileMenu"))
            .ShouldNotBeNull("File menu was not found.");
        var fileMenu = fileMenuElement.AsMenuItem();
        fileMenu.Expand();

        var openMenuItem = WaitForElement(
            () => fileMenu.SubMenu?.Items.FirstOrDefault(item => item.AutomationId == "OpenMenuItem"),
            TimeSpan.FromSeconds(5),
            "the Open menu item");
        openMenuItem.Invoke();

        return WaitForElement(
            () => MainWindow.ModalWindows.FirstOrDefault(window => window.Title == "Choose Repository"),
            TimeSpan.FromSeconds(5),
            "the Choose Repository dialog");
    }

    private static string ResolveExecutablePath()
    {
        var testDirectory = TestContext.CurrentContext.TestDirectory;
        var candidate = Directory.EnumerateFiles(testDirectory, "Arius.Explorer.exe", SearchOption.AllDirectories)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();

        if (candidate is null)
        {
            throw new FileNotFoundException($"Could not locate Arius.Explorer.exe under '{testDirectory}'. Ensure the application project is built before running the UI tests.");
        }

        return candidate;
    }
}
