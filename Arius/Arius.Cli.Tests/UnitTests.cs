﻿using Arius.Core.Commands.Archive;
using Arius.Core.Commands.Restore;
using Arius.Core.Facade;
using Arius.Core.Repositories;
using Azure.Storage.Blobs.Models;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using System;
using System.IO;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;
using WouterVanRanst.Utils.Extensions;

namespace Arius.Cli.Tests;


/* Archive file
 * Archive with directory exists
 * archive with directory not exists
 * archive with invalid tier
 * archive without specifying password
 * 
 *
 */

internal class UnitTests
{
    [Test]
    public async Task Cli_NoCommand_NoErrorCommandOverview()
    {
        Arius.Cli.Utils.AnsiConsoleExtensions.StartNewRecording();

        var r = await Program.Main(Array.Empty<string>());

        var consoleText = Arius.Cli.Utils.AnsiConsoleExtensions.ExportNewText();

        r.Should().Be(0);
        consoleText.Should().ContainAll("USAGE", "COMMANDS", "archive", "restore"/*, "rehydrate"*/);
    }

    [Test]
    public async Task Cli_CommandWithoutParameters_RuntimeException([Values("archive", "restore"/*, "rehydrate"*/)] string command)
    {
        Arius.Cli.Utils.AnsiConsoleExtensions.StartNewRecording();

        var r = await Program.Main(command.AsArray());

        var consoleText = Arius.Cli.Utils.AnsiConsoleExtensions.ExportNewText();

        r.Should().Be(-1);
        consoleText.Should().Contain("Command error:");
        consoleText.Should().NotContain("at "); // no stack trace in the output
    }

    [Test]
    public async Task Cli_CommandExplanation_OK([Values("archive", "restore" /*, "rehydrate"*/)] string command)
    {
        Arius.Cli.Utils.AnsiConsoleExtensions.StartNewRecording();

        var r = await Program.Main($"{command} -h".Split(' '));

        var consoleText = Arius.Cli.Utils.AnsiConsoleExtensions.ExportNewText();

        r.Should().Be(0);
        consoleText.Should().Contain($"arius {command} [PATH] [OPTIONS]");
        consoleText.Should().Contain("-h, --help");
    }

    [Test]
    public async Task Cli_NonExistingCommand_ParseException()
    {
        Arius.Cli.Utils.AnsiConsoleExtensions.StartNewRecording();

        var args = "unexistingcommand";
        var r = await Program.Main(args.Split(' '));

        var consoleText = Arius.Cli.Utils.AnsiConsoleExtensions.ExportNewText();

        r.Should().Be(-1);
        consoleText.Should().Contain("Error: Unknown command");
        consoleText.Should().NotContain("at "); // no stack trace in the output
    }


    [Test]
    public async Task Cli_CommandWithParameters_CommandCalled([Values("archive", "restore"/*, "rehydrate"*/)] string command)
    {
        var (facade, _, repositoryFacade, _, _, executeArchiveCommand, executeRestoreCommandExpr, dispose) = GetMocks();

        if (command == "archive")
        {
            command = new MockedArchiveCommandOptions().ToString();
            await Program.Main(command.Split(' '), services => services.AddSingleton<NewFacade>(facade.Object));

            repositoryFacade.Verify(executeArchiveCommand, Times.Once());
        }
        else if (command == "restore")
        {
            command = new MockedRestoreCommandOptions().ToString();
            await Program.Main(command.Split(' '), services => services.AddSingleton<NewFacade>(facade.Object));

            repositoryFacade.Verify(executeRestoreCommandExpr, Times.Once());
        }
        else
            throw new NotImplementedException();

        repositoryFacade.Verify(dispose, Times.Exactly(1));
        repositoryFacade.VerifyNoOtherCalls();
    }

    [Test]
    public async Task Cli_CommandWithoutAccountNameAndAccountKey_EnvironmentVariablesUsed([Values("archive", "restore"/*, "rehydrate"*/)] string command)
    {
        var accountName = "haha1";
        var accountKey = "haha2";
        Environment.SetEnvironmentVariable(Program.AriusAccountNameEnvironmentVariableName, accountName);
        Environment.SetEnvironmentVariable(Program.AriusAccountKeyEnvironmentVariableName, accountKey);

        var (facade, _, repositoryFacade, forStorageAccountExpr, _,executeArchiveCommand, executeRestoreCommandExpr, dispose) = GetMocks();

        //IRepositoryOptions? po;

        if (command == "archive")
            command = new MockedArchiveCommandOptions() { AccountName = null, AccountKey = null }.ToString();
        else if (command == "restore")
            command = new MockedRestoreCommandOptions() { AccountName = null, AccountKey = null }.ToString();
        else
            throw new NotImplementedException();

        await Program.Main(command.Split(' '), services => services.AddSingleton<NewFacade>(facade.Object));
        facade.Verify(x => x.ForStorageAccount(accountName, accountKey), Times.Once);
    }

    //  Test Logging in Container

    // TEst DB is part of logging

    // TEst overflow file for logigng

    // Errors should be logged in Core, not in CLI

    // "arius" -> no logs

    // "arius archive" -> no logs

    // "arius archive -n aa" --> no logs, specify path

    // "arius archive -n aa ." + Key in env variable --> logs



    //// Cant really test this because Arius.Core is a mock
    //[Test]
    //public async Task Cli_CommandPartialArguments_Error([Values("archive", "restore", "rehydrate")] string command)
    //{
    //    // Remove AccountKey from Env Variables
    //    var accountKey = Environment.GetEnvironmentVariable(Program.AriusAccountKeyEnvironmentVariableName);
    //    Environment.SetEnvironmentVariable(Program.AriusAccountKeyEnvironmentVariableName, null);

    //    AnsiConsole.Record();

    //    int r;

    //    if (command == "archive")
    //    {
    //        var o = new MockedArchiveCommandOptions { AccountKey = null, Passphrase = null, Container = null, Path = new DirectoryInfo(".") };
    //        (r, _) = await ExecuteMockedCommand<IArchiveCommandOptions>(o);
    //    }
    //    else
    //        throw new NotImplementedException();

    //    var consoleText = AnsiConsole.ExportText();

    //    r.Should().Be(-1);
    //    Program.Instance.e.Should().BeOfType<InvalidOperationException>();
    //    consoleText.Should().Contain("Error: ");
    //    consoleText.Should().NotContain("at "); // no stack trace in the output

    //    // Put it back
    //    Environment.SetEnvironmentVariable(Program.AriusAccountKeyEnvironmentVariableName, accountKey);
    //}

    [OneTimeSetUp]
    public void CreateLogsDirectory()
    private (Mock<NewFacade> FacadeMock, Mock<StorageAccountFacade> StorageAccountFacadeMock, Mock<RepositoryFacade> RepositoryFacadeMock, 
        Expression<Func<RepositoryFacade, Task<(int, ArchiveCommandStatistics)>>> ExecuteArchiveCommandExpr,
        Expression<Func<RepositoryFacade, Task<int>>> ExecuteRestoreCommandExpr,
        Expression<Action<RepositoryFacade>> DisposeExpr) GetMocks()
    {
        // Create the /logs folder for unit testing purposes
        // We 'simulate' running in a container (where the /logs MOUNT VOLUME is present) by creating this folder
        if (Environment.GetEnvironmentVariable(RUNNING_IN_CONTAINER) != "true")
        {
            var logs = new DirectoryInfo("/logs");
            logs.Create();
        }
    }
        // Mock IStorageAccountOptions
        var mockStorageAccountOptions = new Mock<IStorageAccountOptions>();
        // Add setup for methods as required.

    [Test]
    public async Task Cli_CommandRunningInContainerPathSpecified_InvalidOperationException([Values("archive", "restore"/*, "rehydrate"*/)] string command)
    {
        string ric = "false";
        try
        {
            ric = Environment.GetEnvironmentVariable(RUNNING_IN_CONTAINER);
            Environment.SetEnvironmentVariable(RUNNING_IN_CONTAINER, "true");
            
            Arius.Cli.Utils.AnsiConsoleExtensions.StartNewRecording();

            int r;
            Exception? e;

            if (command == "archive")
            {
                var o = new MockedArchiveCommandOptions { Path = new DirectoryInfo(".") };
                (r, _, e) = await ExecuteMockedCommand<IArchiveCommandOptions>(o);
            }
            else if (command == "restore")
            {
                var o = new MockedRestoreCommandOptions { Path = new DirectoryInfo(".") };
                (r, _, e) = await ExecuteMockedCommand<IRestoreCommandOptions>(o);
            }
            else
                throw new NotImplementedException();

            var consoleText = Arius.Cli.Utils.AnsiConsoleExtensions.ExportNewText(); // AnsiConsole.ExportText();

            r.Should().Be(-1);
            e.Should().BeOfType<InvalidOperationException>();
            consoleText.Should().Contain("Error: ");
            consoleText.Should().NotContain("at "); // no stack trace in the output
        }
        finally
        {
            Environment.SetEnvironmentVariable(RUNNING_IN_CONTAINER, ric);
        }
    }
        
        // Mock IRepositoryOptions
        var mockRepositoryOptions = new Mock<IRepositoryOptions>();
        // Add setup for methods as required.

    [Test]
    public async Task Cli_CommandRunningInContainerPathNotSpecified_RootArchivePathUsed([Values("archive", "restore"/*, "rehydrate"*/)] string command)
    {
        string ric = "false";
        try
        {
            ric = Environment.GetEnvironmentVariable(RUNNING_IN_CONTAINER);
            Environment.SetEnvironmentVariable(RUNNING_IN_CONTAINER, "true");

            if (command == "archive")
            {
                var o = new MockedArchiveCommandOptions { Path = null };
                var (_, po, _) = await ExecuteMockedCommand<IArchiveCommandOptions>(o);
                po.Path.FullName.Should().Be(new DirectoryInfo("/archive").FullName);
            }
            else if (command == "restore")
            {
                var o = new MockedRestoreCommandOptions { Path = null };
                var (_, po, _) = await ExecuteMockedCommand<IRestoreCommandOptions>(o);
                po.Path.FullName.Should().Be(new DirectoryInfo("/archive").FullName);
            }
            else
                throw new NotImplementedException();
        }
        finally
        {
            Environment.SetEnvironmentVariable(RUNNING_IN_CONTAINER, ric);
        }
    }
        
        // Mock Repository
        var mockRepository = new Mock<Repository>();
        // Add setup for methods as required.


        // Mock RepositoryFacade
        var mockRepositoryFacade = new Mock<RepositoryFacade>();
        //mockRepositoryFacade.Setup(x => x.GetVersions()).Returns(new List<string> { "v1", "v2" }.ToAsyncEnumerable());

        Expression<Func<RepositoryFacade, Task<(int, ArchiveCommandStatistics)>>> executeArchiveCommandAsyncExpr = x => x.ExecuteArchiveCommandAsync(It.IsAny<DirectoryInfo>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<AccessTier>(), It.IsAny<bool>(), It.IsAny<DateTime>());
        mockRepositoryFacade.Setup(executeArchiveCommandAsyncExpr)
            .ReturnsAsync((1, new ArchiveCommandStatistics()))
            .Verifiable();

    private async Task<(int ExitCode, T? ParsedOptions, Exception? Exception)> ExecuteMockedCommand<T>(T aco) where T : class, ICommandOptions
    {
        // Create Mock
        var validateReturnMock = new Mock<FluentValidation.Results.ValidationResult>();
        Expression<Func<RepositoryFacade, Task<int>>> executeRestoreCommandAsyncExpr = x => x.ExecuteRestoreCommandAsync(It.IsAny<DirectoryInfo>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<DateTime>());
        mockRepositoryFacade.Setup(executeRestoreCommandAsyncExpr)
            .ReturnsAsync(1)
            .Verifiable();

        Expression<Func<Core.Commands.ICommand<T>, FluentValidation.Results.ValidationResult>> validateExpr = m => m.Validate(It.IsAny<T>());
        Expression<Func<Core.Commands.ICommand<T>, Task<int>>> executeAsyncExpr = m => m.ExecuteAsync(It.IsAny<T>());
        //mockRepositoryFacade.Setup(x => x.ExecuteRehydrateCommandAsync())
        //    .ReturnsAsync(1);

        var commandMock = new Mock<Core.Commands.ICommand<T>>();
        commandMock.Setup(validateExpr).Returns(validateReturnMock.Object);
        commandMock.Setup(executeAsyncExpr).Verifiable();
        Expression<Action<RepositoryFacade>> disposeExpr = x => x.Dispose(It.IsAny<bool>());
        mockRepositoryFacade.Setup(disposeExpr)
            .Verifiable();

        // Run Arius
        var (r, po, e) = Program.InternalMain(aco.ToString().Split(' '), sc => AddMockedAriusCoreCommands<T>(sc, commandMock.Object));

        if (r == 0)
        {
            e.Should().BeNull();
        // Mock StorageAccountFacade
        var mockStorageAccountFacade = new Mock<StorageAccountFacade>();
        //mockStorageAccountFacade.Setup(x => x.GetContainerNamesAsync()).ReturnsAsync(new List<string> { "test1", "test2" }.ToAsyncEnumerable());


        Expression<Func<StorageAccountFacade, Task<RepositoryFacade>>> forRepositoryAsyncExpr = x => x.ForRepositoryAsync(It.IsAny<string>(), It.IsAny<string>());
        mockStorageAccountFacade.Setup(forRepositoryAsyncExpr)
            .ReturnsAsync(mockRepositoryFacade.Object)
            .Verifiable();
        //mockStorageAccountFacade.Setup(x => x.ForRepositoryAsync(It.IsAny<IRepositoryOptions>())).ReturnsAsync(mockRepositoryFacade.Object);

        // Mock NewFacade
        var mockNewFacade = new Mock<NewFacade>();
        Expression<Func<NewFacade, StorageAccountFacade>> forStorageAccountExpr = x => x.ForStorageAccount(It.IsAny<string>(), It.IsAny<string>());
        mockNewFacade.Setup(forStorageAccountExpr)
            //.Callback<string, string>((an, ak) => (passedAccountName, passedAccountKey) = (an, ak))
            .Returns(mockStorageAccountFacade.Object)
            .Verifiable();

        //mockNewFacade.Verify();
            
        //mockNewFacade.Setup(x => x.ForStorageAccount(It.IsAny<IStorageAccountOptions>())).Returns(mockStorageAccountFacade.Object);

        return (mockNewFacade, mockStorageAccountFacade, mockRepositoryFacade,
            forStorageAccountExpr, forRepositoryAsyncExpr,
            executeArchiveCommandAsyncExpr, executeRestoreCommandAsyncExpr, disposeExpr);
    }

    private class MockedArchiveCommandOptions : IArchiveCommandOptions
    {
        public string AccountName { get; init; } = "an";
        public string AccountKey { get; init; } = "ak";
        public string ContainerName { get; init; } = "c";
        public string Passphrase { get; init; } = "pp";
        public bool FastHash { get; init; } = false;
        public bool RemoveLocal { get; init; } = false;
        public AccessTier Tier { get; init; } = AccessTier.Cool;
        public bool Dedup { get; init; } = false;
        public DirectoryInfo? Path { get; init; } = new DirectoryInfo(".");
        public DateTime VersionUtc { get; init; } = DateTime.UtcNow;

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

            sb.Append($"--tier {Tier.ToString().ToLower()} ");

            if (Dedup)
                sb.Append("--dedup ");

            if (FastHash)
                sb.Append("--fasthash");

            if (Path is not null)
                sb.Append($"{Path}");

            return sb.ToString();
        }
    }

    private class MockedRestoreCommandOptions : IRestoreCommandOptions
    {
        public string AccountName { get; init; } = "an";
        public string AccountKey { get; init; } = "ak";
        public string ContainerName { get; init; } = "c";
        public string Passphrase { get; init; } = "pp";
        public bool Synchronize { get; init; } = false;
        public bool Download { get; init; } = false;
        public bool KeepPointers { get; init; } = false;
        public DirectoryInfo? Path { get; init; } = new DirectoryInfo(".");
        public DateTime? PointInTimeUtc { get; init; }

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

            return sb.ToString();
        }
    }
}