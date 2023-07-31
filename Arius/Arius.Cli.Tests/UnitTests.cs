using Arius.Core.Commands;
using Arius.Core.Commands.Archive;
using Arius.Core.Commands.Restore;
using Azure.Storage.Blobs.Models;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Spectre.Console.Cli;
using System;
using System.IO;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

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
    private const string RUNNING_IN_CONTAINER = "DOTNET_RUNNING_IN_CONTAINER";
    
    [Test]
    public async Task Cli_NoCommand_NoErrorCommandOverview()
    {
        Arius.Cli.Utils.AnsiConsoleExtensions.StartNewRecording();

        var args = "";
        var (r, _, _) = Program.InternalMain(args.Split(' '));

        var consoleText = Arius.Cli.Utils.AnsiConsoleExtensions.ExportNewText();

        r.Should().Be(0);
        consoleText.Should().ContainAll("USAGE", "COMMANDS", "archive", "restore"/*, "rehydrate"*/);
    }

    [Test]
    public async Task Cli_CommandWithoutParameters_RuntimeException([Values("archive", "restore"/*, "rehydrate"*/)] string command)
    {
        Arius.Cli.Utils.AnsiConsoleExtensions.StartNewRecording();

        var (r, _, e) = Program.InternalMain(command.Split(' '));

        var consoleText = Arius.Cli.Utils.AnsiConsoleExtensions.ExportNewText();

        r.Should().Be(-1);
        e.Should().BeOfType<CommandRuntimeException>();
        consoleText.Should().Contain("Command error:");
        consoleText.Should().NotContain("at "); // no stack trace in the output
    }

    [Test]
    public async Task Cli_NonExistingCommand_ParseException()
    {
        Arius.Cli.Utils.AnsiConsoleExtensions.StartNewRecording();

        var args = "unexistingcommand";
        var (r, _, e) = Program.InternalMain(args.Split(' '));

        var consoleText = Arius.Cli.Utils.AnsiConsoleExtensions.ExportNewText();

        r.Should().Be(-1);
        e.Should().BeOfType<CommandParseException>();
        consoleText.Should().Contain("Error: ");
        consoleText.Should().NotContain("at "); // no stack trace in the output
    }


    [Test]
    public async Task Cli_CommandWithParameters_CommandCalled([Values("archive", "restore"/*, "rehydrate"*/)] string command)
    {
        if (command == "archive")
        {
            var o = new MockedArchiveCommandOptions();
            await ExecuteMockedCommand<IArchiveCommandOptions>(o);
        }
        else if (command == "restore")
        {
            var o = new MockedRestoreCommandOptions();
            await ExecuteMockedCommand<IRestoreCommandOptions>(o);
        }
        else
            throw new NotImplementedException();
    }

    [Test]
    public async Task Cli_CommandWithoutAccountNameAndAccountKey_EnvironmentVariablesUsed([Values("archive", "restore"/*, "rehydrate"*/)] string command)
    {
        var accountName = "haha1";
        var accountKey = "haha2";
        Environment.SetEnvironmentVariable(Program.AriusAccountNameEnvironmentVariableName, accountName);
        Environment.SetEnvironmentVariable(Program.AriusAccountKeyEnvironmentVariableName, accountKey);

        IRepositoryOptions? po;

        if (command == "archive")
        {
            var o = new MockedArchiveCommandOptions { AccountName = null, AccountKey = null };
            (_, po, _) = await ExecuteMockedCommand<IArchiveCommandOptions>(o);
        }
        else if (command == "restore")
        {
            var o = new MockedRestoreCommandOptions { AccountName = null, AccountKey = null };
            (_, po, _) = await ExecuteMockedCommand<IRestoreCommandOptions>(o);
        }
        else
            throw new NotImplementedException();

        po.AccountName.Should().Be(accountName);
        po.AccountKey.Should().Be(accountKey);
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
    {
        // Create the /logs folder for unit testing purposes
        // We 'simulate' running in a container (where the /logs MOUNT VOLUME is present) by creating this folder
        if (Environment.GetEnvironmentVariable(RUNNING_IN_CONTAINER) != "true")
        {
            var logs = new DirectoryInfo("/logs");
            logs.Create();
        }
    }

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




    private async Task<(int ExitCode, T? ParsedOptions, Exception? Exception)> ExecuteMockedCommand<T>(T aco) where T : class, ICommandOptions
    {
        // Create Mock
        var validateReturnMock = new Mock<FluentValidation.Results.ValidationResult>();
        validateReturnMock.Setup(m => m.IsValid).Returns(true);

        Expression<Func<Core.Commands.ICommand<T>, FluentValidation.Results.ValidationResult>> validateExpr = m => m.Validate(It.IsAny<T>());
        Expression<Func<Core.Commands.ICommand<T>, Task<int>>> executeAsyncExpr = m => m.ExecuteAsync(It.IsAny<T>());

        var commandMock = new Mock<Core.Commands.ICommand<T>>();
        commandMock.Setup(validateExpr).Returns(validateReturnMock.Object);
        commandMock.Setup(executeAsyncExpr).Verifiable();

        // Run Arius
        var (r, po, e) = Program.InternalMain(aco.ToString().Split(' '), sc => AddMockedAriusCoreCommands<T>(sc, commandMock.Object));

        if (r == 0)
        {
            e.Should().BeNull();

            //archiveCommandMock.Verify(validateExpr, Times.Exactly(1));
            commandMock.Verify(executeAsyncExpr, Times.Exactly(1));
            //archiveCommandMock.VerifyNoOtherCalls();
        }
        else
            e.Should().NotBeNull();

        return (r, (T?)po, e);
    }
    private IServiceCollection AddMockedAriusCoreCommands<T>(IServiceCollection services, Core.Commands.ICommand<T> a) where T : class, ICommandOptions
    {
        services.AddSingleton(a);
        
        return services;
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