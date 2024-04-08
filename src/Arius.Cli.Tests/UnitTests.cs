using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Arius.Cli.Utils;
using Arius.Core.Commands;
using Arius.Core.Commands.Archive;
using Arius.Core.Facade;
using Azure.Storage.Blobs.Models;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;
using WouterVanRanst.Utils.Extensions;

namespace Arius.Cli.Tests;


/* TODO
 *
 * Cli_ArchiveCommand_LogContainsDB()
 * Test Logging in Container
 *
 * TEst DB is part of logging
 * TEst overflow file for logigng
 * Errors should be logged in Core, not in CLI
 * "arius" -> no logs
 * "arius archive" -> no logs
 * "arius archive -n aa" --> no logs, specify path
 * "arius archive -n aa ." + Key in env variable --> logs
 *
 * Archive file
 * Archive with directory exists
 * archive with directory not exists
 * archive with invalid tier
 * archive without specifying password
 *
 */

internal class UnitTests
{
    private const string RUNNING_IN_CONTAINER = "DOTNET_RUNNING_IN_CONTAINER";
    private static readonly bool IsRunningInContainer = Environment.GetEnvironmentVariable(RUNNING_IN_CONTAINER) == "true";

    [OneTimeSetUp]
    public void CreateLogsDirectory()
    {
        try
        {
            // Create the /logs folder for unit testing purposes
            // We 'simulate' running in a container (where the /logs MOUNT VOLUME is present) by creating this folder
            if (!IsRunningInContainer)
            {
                Directory.CreateDirectory("/logs");
                File.Create(Path.Combine("/logs", "Used_for_Arius_unit_test.txt"));
            }

            Directory.CreateDirectory("/archive");
            File.Create(Path.Combine("/archive", "Used_for_Arius_unit_test.txt"));
        }
        catch (IOException)
        {
            // this does not work in the Github Actions runner
        }
        catch (UnauthorizedAccessException)
        {
            // this does not work in the Github Actions runner
        }
    }

    [Test]
    public async Task Cli_NoCommand_NoErrorCommandOverview()
    {
        AnsiConsoleExtensions.StartNewRecording();

        var r = await Program.Main(Array.Empty<string>());

        var consoleText = AnsiConsoleExtensions.ExportNewText();

        r.Should().Be(0);
        consoleText.Should().ContainAll("USAGE", "COMMANDS", "archive", "restore"/*, "rehydrate"*/);
    }

    [Test]
    public async Task Cli_CommandWithoutParameters_RuntimeException([Values("archive", "restore"/*, "rehydrate"*/)] string command)
    {
        AnsiConsoleExtensions.StartNewRecording();

        var r = await Program.Main(command.AsArray());

        var consoleText = AnsiConsoleExtensions.ExportNewText();

        r.Should().Be(-1);
        consoleText.Should().Contain("Command error:");
        consoleText.Should().NotContain("at "); // no stack trace in the output
    }

    [Test]
    public async Task Cli_CommandExplanation_OK([Values("archive", "restore" /*, "rehydrate"*/)] string command)
    {
        AnsiConsoleExtensions.StartNewRecording();

        var r = await Program.Main($"{command} -h".Split(' '));

        var consoleText = AnsiConsoleExtensions.ExportNewText();

        r.Should().Be(0);
        consoleText.Should().Contain($"arius {command} [PATH] [OPTIONS]");
        consoleText.Should().Contain("-h, --help");
    }

    [Test]
    public async Task Cli_NonExistingCommand_ParseException()
    {
        AnsiConsoleExtensions.StartNewRecording();

        var args = "unexistingcommand";
        var r = await Program.Main(args.Split(' '));

        var consoleText = AnsiConsoleExtensions.ExportNewText();

        r.Should().Be(-1);
        consoleText.Should().Contain("Error: Unknown command");
        consoleText.Should().NotContain("at "); // no stack trace in the output
    }


    [Test]
    public async Task Cli_CommandWithParameters_CommandCalled([Values("archive", "restore"/*, "rehydrate"*/)] string command)
    {
        var (facade, repositoryFacade) = GetSubstitutes();

        if (command == "archive")
        {
            await Program.Main(command.Split(' '), services => services.AddSingleton<Facade>(facade));
            command = new MockedArchiveCommandOptions().ToString();

            Received.InOrder(() => repositoryFacade.ExecuteArchiveCommandAsync(Arg.Any<DirectoryInfo>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<DateTime>()));
        }
        else if (command == "restore")
        {
            command = new MockedRestoreCommandOptions { Synchronize = true }.ToString();
            await Program.Main(command.Split(' '), services => services.AddSingleton<Facade>(facade));

            Received.InOrder(() => repositoryFacade.ExecuteRestoreCommandAsync(Arg.Any<DirectoryInfo>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<DateTime>()));
        }
        else
            throw new NotImplementedException();

        repositoryFacade.Received(1).Dispose(Arg.Any<bool>());
        repositoryFacade.DidNotReceiveWithAnyArgs().ExecuteRehydrateCommandAsync();
    }


    [Test]
    public async Task Cli_CommandWithoutAccountNameAndAccountKey_EnvironmentVariablesUsed([Values("archive", "restore"/*, "rehydrate"*/)] string command)
    {
        var accountName = "haha1";
        var accountKey  = "haha2";
        Environment.SetEnvironmentVariable(Program.AriusAccountNameEnvironmentVariableName, accountName);
        Environment.SetEnvironmentVariable(Program.AriusAccountKeyEnvironmentVariableName, accountKey);

        var (facade, _) = GetSubstitutes();

        if (command == "archive")
            command = new MockedArchiveCommandOptions { AccountName = null, AccountKey = null }.ToString();
        else if (command == "restore")
            command = new MockedRestoreCommandOptions { AccountName = null, AccountKey = null, Synchronize = true }.ToString();
        else
            throw new NotImplementedException();

        await Program.Main(command.Split(' '), services => services.AddSingleton(facade));

        // Verify that ForStorageAccount was called with specific arguments
        facade.Received(1).ForStorageAccount(accountName, accountKey);
    }

    [Test]
    public async Task Cli_CommandPartialArguments_Error([Values("archive", "restore"/*, "rehydrate"*/)] string command)
    {
        // Remove AccountKey from Env Variables
        var accountKey = Environment.GetEnvironmentVariable(Program.AriusAccountKeyEnvironmentVariableName);
        Environment.SetEnvironmentVariable(Program.AriusAccountKeyEnvironmentVariableName, null);

        AnsiConsoleExtensions.StartNewRecording();

        int r;

        if (command == "archive")
            command = new MockedArchiveCommandOptions { AccountKey = null, Passphrase = null, ContainerName = null }.ToString();
        else if (command == "restore")
            command = new MockedRestoreCommandOptions { AccountKey = null, Passphrase = null, ContainerName = null, Synchronize = true }.ToString();
        else
            throw new NotImplementedException();

        r = await Program.Main(command.Split(' '));
        var consoleText = AnsiConsoleExtensions.ExportNewText();

        r.Should().Be(-1);
        //Program.Instance.e.Should().BeOfType<InvalidOperationException>();
        consoleText.Should().Contain("Command error: AccountKey must be specified");
        consoleText.Should().NotContain("at "); // no stack trace in the output

        // Put it back
        Environment.SetEnvironmentVariable(Program.AriusAccountKeyEnvironmentVariableName, accountKey);
    }

    [Test]
    public async Task Cli_CommandRunningInContainerPathSpecified_InvalidOperationException([Values("archive", "restore"/*, "rehydrate"*/)] string command)
    {
        string ric = "false";
        try
        {
            ric = Environment.GetEnvironmentVariable(RUNNING_IN_CONTAINER);
            Environment.SetEnvironmentVariable(RUNNING_IN_CONTAINER, "true");

            AnsiConsoleExtensions.StartNewRecording();

            int r;
            //Exception? e;

            if (command == "archive")
            {
                command = new MockedArchiveCommandOptions { Path = new DirectoryInfo(".") /* path is explicitly set */ }.ToString();
                r       = await Program.Main(command.Split(' '));
            }
            else if (command == "restore")
            {
                command = new MockedRestoreCommandOptions { Path = new DirectoryInfo(".") /* path is explicitly set */ }.ToString();
                r       = await Program.Main(command.Split(' '));
            }
            else
                throw new NotImplementedException();

            var consoleText = AnsiConsoleExtensions.ExportNewText();

            r.Should().Be(-1);
            //e.Should().BeOfType<InvalidOperationException>();
            consoleText.Should().Contain("Error: ");
            consoleText.Should().NotContain("at "); // no stack trace in the output
        }
        finally
        {
            Environment.SetEnvironmentVariable(RUNNING_IN_CONTAINER, ric);
        }
    }

    [Test]
    public async Task Cli_CommandRunningInContainerPathNotSpecified_RootArchivePathUsed([Values("archive", "restore"/*, "rehydrate"*/)] string command)
    {
        var (facade, repositoryFacade) = GetSubstitutes();

        string ric = "false";
        try
        {
            ric = Environment.GetEnvironmentVariable(RUNNING_IN_CONTAINER);
            Environment.SetEnvironmentVariable(RUNNING_IN_CONTAINER, "true");

            if (command == "archive")
            {
                var o = new MockedArchiveCommandOptions { Path = null };
                await Program.Main(o.ToString().Split(' '), services => services.AddSingleton<Facade>(facade));
                repositoryFacade.Received(1).ExecuteArchiveCommandAsync(Arg.Is<DirectoryInfo>(di => di.FullName == new DirectoryInfo("/archive").FullName), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<DateTime>());
            }
            else if (command == "restore")
            {
                var o = new MockedRestoreCommandOptions { Path = null, Synchronize = true };
                await Program.Main(o.ToString().Split(' '), services => services.AddSingleton<Facade>(facade));
                repositoryFacade.Received(1).ExecuteRestoreCommandAsync(Arg.Is<DirectoryInfo>(di => di.FullName == new DirectoryInfo("/archive").FullName), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<DateTime>());
            }
            else
                throw new NotImplementedException();
        }
        finally
        {
            Environment.SetEnvironmentVariable(RUNNING_IN_CONTAINER, ric);
        }
    }


    private static (Facade, RepositoryFacade) GetSubstitutes()
    {
        // Substitute RepositoryFacade
        var repositoryFacade = Substitute.For<RepositoryFacade>();
        repositoryFacade.ExecuteArchiveCommandAsync(Arg.Any<DirectoryInfo>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<DateTime>())
            .Returns(Task.FromResult((CommandResultStatus.Success, new ArchiveCommandStatistics())));
        repositoryFacade.ExecuteRestoreCommandAsync(Arg.Any<DirectoryInfo>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<DateTime>())
            .Returns(Task.FromResult(CommandResultStatus.Success));

        // Substitute StorageAccountFacade
        var storageAccountFacade = Substitute.For<StorageAccountFacade>();
        storageAccountFacade.ForRepositoryAsync(Arg.Any<string>(), Arg.Any<string>())
            .Returns(Task.FromResult(repositoryFacade));

        // Substitute Facade
        var facade = Substitute.For<Facade>();
        facade
            .ForStorageAccount(Arg.Any<string>(), Arg.Any<string>())
            .Returns(storageAccountFacade);

        return (facade, repositoryFacade);
    }


    private class MockedArchiveCommandOptions
    {
        public string?        AccountName   { get; init; } = "an";
        public string?        AccountKey    { get; init; } = "ak";
        public string         ContainerName { get; init; } = "c";
        public string         Passphrase    { get; init; } = "pp";
        public bool           FastHash      { get; init; } = false;
        public bool           RemoveLocal   { get; init; } = false;
        public AccessTier     Tier          { get; init; } = AccessTier.Cool;
        public bool           Dedup         { get; init; } = false;
        public DirectoryInfo? Path          { get; init; } = IsRunningInContainer ? null : new DirectoryInfo(".");
        public DateTime       VersionUtc    { get; init; } = DateTime.UtcNow;

        public override string ToString()
        {
            var sb = new StringBuilder("archive ");

            if (AccountName is not null)
                sb.Append($"-n {AccountName} ");

            if (AccountKey is not null)
                sb.Append($"-k {AccountKey} ");

            if (Passphrase is not null)
                sb.Append($"-p {Passphrase} ");

            if (ContainerName is not null)
                sb.Append($"-c {ContainerName} ");

            if (RemoveLocal)
                sb.Append("--remove-local ");

            sb.Append($"--tier {Tier.ToString().ToLower()} "); // NOTE the tolower is intentional - AccessTier doesnt always play nice and (AccessTier)"cool" is also valid

            if (Dedup)
                sb.Append("--dedup ");

            if (FastHash)
                sb.Append("--fasthash");

            if (Path is not null)
                sb.Append($"{Path}");

            return sb.ToString().Trim();
        }
    }

    private class MockedRestoreCommandOptions
    {
        public string?        AccountName    { get; init; } = "an";
        public string?        AccountKey     { get; init; } = "ak";
        public string         ContainerName  { get; init; } = "c";
        public string         Passphrase     { get; init; } = "pp";
        public bool           Synchronize    { get; init; } = false;
        public bool           Download       { get; init; } = false;
        public bool           KeepPointers   { get; init; } = false;
        public DirectoryInfo? Path           { get; init; } = IsRunningInContainer ? null : new DirectoryInfo(".");
        public DateTime       PointInTimeUtc { get; init; }

        public override string ToString()
        {
            var sb = new StringBuilder("restore ");

            if (AccountName is not null)
                sb.Append($"-n {AccountName} ");

            if (AccountKey is not null)
                sb.Append($"-k {AccountKey} ");

            sb.Append(
                $"-p {Passphrase} " +
                $"-c {ContainerName} " +
                $"{(Synchronize ? "--synchronize " : "")}" +
                $"{(Download ? "--download " : "")}" +
                $"{(KeepPointers ? "--keep-pointers " : "")}" +
                $"{Path}");

            return sb.ToString().Trim();
        }
    }
}