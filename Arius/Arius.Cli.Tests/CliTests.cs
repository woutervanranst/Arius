using Arius.Cli;
using Arius.Core.Commands;
using Arius.Core.Commands.Archive;
using Azure.Storage.Blobs.Models;
using FluentAssertions;
using FluentAssertions.Common;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Spectre.Console.Cli;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

class CliTests
{
    [Test]
    public async Task Cli_NoCommand_NoError()
    {
        var args = "";

        var r = await Program.Main(args.Split(' '));

        r.Should().Be(0);
    }


    [Test]
    public async Task Cli_ArchiveCommandWithoutParameters_ParseErrorResult()
    {
        var args = "archive";

        var r = await Program.Main(args.Split(' '));

        r.Should().Be(-1);
        Program.Instance.e.Should().BeOfType<CommandRuntimeException>();

        //Assert.IsInstanceOf<ParseErrorResult>(p.InvocationContext.InvocationResult);
        //Assert.AreEqual(1, p.InvocationContext.ParseResult.Errors.Count(pe => pe.Message == "Required argument missing for command: archive"));
        //Assert.AreEqual((int)Program.ExitCode.ERROR, Environment.ExitCode);
    }

    //[Test]
    //public async Task Cli_RestoreCommandWithoutParameters_ParseErrorResult()
    //{
    //    var args = "restore";

    //    var p = new Program();
    //    await p.Main(args.Split(' '));

    //    Assert.IsInstanceOf<ParseErrorResult>(p.InvocationContext.InvocationResult);
    //    Assert.AreEqual(1, p.InvocationContext.ParseResult.Errors.Count(pe => pe.Message == "Required argument missing for command: restore"));
    //    Assert.AreEqual((int)Program.ExitCode.ERROR, Environment.ExitCode);
    //}


    //[Test]
    //public async Task Cli_NonExistingCommand_ParseErrorResult()
    //{
    //    var args = "unexistingcommand";

    //    var p = new Program();
    //    await p.Main(args.Split(' '));

    //    Assert.IsInstanceOf<ParseErrorResult>(p.InvocationContext.InvocationResult);
    //    Assert.AreEqual(1, p.InvocationContext.ParseResult.Errors.Count(pe => pe.Message == $"Unrecognized command or argument '{args}'"));
    //    Assert.AreEqual((int)Program.ExitCode.ERROR, Environment.ExitCode);
    //}


    [Test]
    public async Task Cli_ArchiveCommandWithParameters_CommandCalled()
    {
        var aco = new ArchiveCommandOptions();

        await ExecuteMockedArchiveCommand(aco);
    }

    private async Task<Program> ExecuteMockedArchiveCommand(IArchiveCommandOptions aco)
    {
        // Create Mock
        var validateReturnMock = new Mock<ValidationResult>();
        validateReturnMock.Setup(m => m.IsValid).Returns(true);

        Expression<Func<Core.Commands.ICommand<IArchiveCommandOptions>, ValidationResult>> validateExpr = m => m.Validate(It.IsAny<IArchiveCommandOptions>());
        Expression<Func<Core.Commands.ICommand<IArchiveCommandOptions>, Task<int>>> executeAsyncExpr = m => m.ExecuteAsync(It.IsAny<IArchiveCommandOptions>());

        var archiveCommandMock = new Mock<Core.Commands.ICommand<IArchiveCommandOptions>>();
        archiveCommandMock.Setup(validateExpr).Returns(validateReturnMock.Object);
        archiveCommandMock.Setup(executeAsyncExpr).Verifiable();

        // Run Arius
        var p = new Program();
        var r = await p.Main(aco.ToString().Split(' '), sc => AddMockedAriusCoreCommands(sc, archiveCommandMock.Object));

        // Assert
        r.Should().Be(0);

        //archiveCommandMock.Verify(validateExpr, Times.Exactly(1));
        archiveCommandMock.Verify(executeAsyncExpr, Times.Exactly(1));
        //archiveCommandMock.VerifyNoOtherCalls();

        return p;
    }

    [Test]
    public async Task Cli_ArchiveCommandWithoutAccountNameAndAccountKey_EnviornmentVariablesUsed()
    {
        var aco = new ArchiveCommandOptions { AccountName = null, AccountKey = null };
        
        var accountName = "haha1";
        var accountKey = "haha2";
        Environment.SetEnvironmentVariable(Program.AriusAccountNameEnvironmentVariableName, accountName);
        Environment.SetEnvironmentVariable(Program.AriusAccountKeyEnvironmentVariableName, accountKey);

        var p = await ExecuteMockedArchiveCommand(aco);

        var po = (IArchiveCommandOptions)p.ParsedOptions;

        po.AccountName.Should().Be(accountName);
        po.AccountKey.Should().Be(accountKey);
    }







    //[Test]
    //public async Task Cli_RestoreCommandWithParameters_FacadeCalled()
    //{
    //    CreateRestoreCommand(out string accountName, out string accountKey, out string container, out string passphrase, out bool synchronize, out bool download, out bool keepPointers, out string path, out string cmd);

    //    Expression<Func<IFacade, Core.Commands.ICommand>> expr = (m) => m.CreateRestoreCommand(accountName, accountKey, container, passphrase, synchronize, download, keepPointers, path, DateTime.UtcNow);

    //    var r = await ExecuteMainWithMockedFacade(cmd, expr);

    //    Assert.AreEqual(0, Environment.ExitCode);

    //    r.MockFacade.Verify(expr, Times.Exactly(1));
    //    r.MockFacade.VerifyNoOtherCalls();


    //    // TODO Directory.Exists
    //    //var w = r.InvocationContext.InvocationResult;
    //    // TODO Facade throws error  
    //    // todo restore with file and DirectoryInfo

    //    //var macb = new Mock<Facade.ArchiveCommandBuilder>();
    //    //macb.Setup(m => m.ForStorageAccount(accountName, accountKey)).Returns(macb.Object);
    //    //macb.Setup(m => m.ForContainer(container)).Returns(macb.Object);
    //    //mfb.Setup(m => m.GetArchiveCommandBuilder()).Returns(macb.Object);



    //    //var x = mf.GetArchiveCommandBuilder()
    //    //    .ForStorageAccount(accountName, "h");


    //    //var myViewModel = TheOutletViewModelForTesting();
    //    //var mockBuilder = new Mock<IOutletViewModelBuilder>();

    //    //mockBuilder.Setup(m => m.WithOutlet(It.IsAny<int>())).Returns(mockBuilder.Object);
    //    //mockBuilder.Setup(m => m.WithCountryList()).Returns(mockBuilder.Object);
    //    //mockBuilder.Setup(m => m.Build()).Returns(myViewModel);


    //    //Facade f = m;

    //    //m.Setup(f => f.CreateArchiveCommand)


    //    /*
    //     * 
    //     * 
    //     * private async Task<IServiceProvider> ArchiveCommand(AccessTier tier, bool removeLocal = false, bool fastHash = false, bool dedup = false)
    //{
    //    var cmd = "archive " +
    //        $"-n {TestSetup.AccountName} " +
    //        $"-k {TestSetup.AccountKey} " +
    //        $"-p {TestSetup.passphrase} " +
    //        $"-c {TestSetup.container.Name} " +
    //        $"{(removeLocal ? "--remove-local " : "")}" +
    //        $"--tier {tier.ToString().ToLower()} " +
    //        $"{(dedup ? "--dedup " : "")}" +
    //        $"{(fastHash ? "--fasthash" : "")}" +
    //        $"{TestSetup.archiveTestDirectory.FullName}";

    //    return await ExecuteCommand(cmd);   
    //}

    //private async Task<IServiceProvider> ExecuteCommand(string cmd)
    //{
    //    Environment.SetEnvironmentVariable(Arius.AriusCommandService.CommandLineEnvironmentVariableName, cmd);

    //    //Action<IConfigurationBuilder> bla = (b) =>
    //    //{
    //    //    b.AddInMemoryCollection(new Dictionary<string, string> {
    //    //            { "TempDir:TempDirectoryName", ".ariustemp2" }
    //    //        });
    //    //};

    //    await Arius.Program.CreateHostBuilder(cmd.Split(' '), null).RunConsoleAsync();

    //    if (Environment.ExitCode != 0)
    //        throw new ApplicationException("Exitcode is not 0");

    //    var sp = TestSetup.GetServiceProvider();
    //    return sp;
    //}
    //     * 
    //     */
    //}

    

    private IServiceCollection AddMockedAriusCoreCommands(IServiceCollection services, Arius.Core.Commands.ICommand<IArchiveCommandOptions> m)
    {
        services.AddSingleton(m);
        return services;
    }

    private class ArchiveCommandOptions : IArchiveCommandOptions
    {
        public string AccountName { get; init; } = "an";
        public string AccountKey { get; init; } = "ak";
        public string Container { get; init; } = "c";
        public string Passphrase { get; init; } = "pp";
        public bool FastHash { get; init; } = false;
        public bool RemoveLocal { get; init; } = false;
        public AccessTier Tier { get; init; } = AccessTier.Cool;
        public bool Dedup { get; init; } = false;
        public DirectoryInfo Path { get; init; } = new DirectoryInfo(".");
        public DateTime VersionUtc { get; init; } = DateTime.UtcNow;

        public override string ToString()
        {
            var sb = new StringBuilder("archive ");

            if (AccountName is not null)
                sb.Append($"-n {AccountName} ");

            if (AccountKey is not null)
                sb.Append($"-k {AccountKey} ");

            sb.Append(
              $"-p {Passphrase} " +
              $"-c {Container} " +
              $"{(RemoveLocal ? "--remove-local " : "")}" +
              $"--tier {Tier.ToString().ToLower()} " +
              $"{(Dedup ? "--dedup " : "")}" +
              $"{(FastHash ? "--fasthash" : "")}" +
              $"{Path}");

            return sb.ToString();
        }
    }

    //private static void CreateRestoreCommand(out string accountName, out string accountKey, out string container, out string passphrase, out bool synchronize, out bool download, out bool keepPointers, out string path, out string cmd)
    //{
    //    accountName = "ha";
    //    accountKey = "ha";
    //    container = "h";
    //    passphrase = "3";
    //    synchronize = false;
    //    download = false;
    //    keepPointers = false;
    //    path = "he";

    //    cmd = "restore " +
    //          $"-n {accountName} " +
    //          $"-k {accountKey} " +
    //          $"-p {passphrase} " +
    //          $"-c {container} " +
    //          $"{(synchronize ? "--synchronize " : "")}" +
    //          $"{(download ? "--download " : "")}" +
    //          $"{(keepPointers ? "--keep-pointers " : "")}" +
    //          $"{path}";
    //}

    //private static async Task<(Mock<IFacade> MockFacade, InvocationContext InvocationContext)> ExecuteMainWithMockedFacade(string args, Expression<Func<IFacade, Core.Commands.ICommand>> mockedFacadeMethod = null)
    //{
    //    Expression<Func<ICommand, Task<int>>> executeExpression = (c) => c.Execute();
    //    var mcb = new Mock<ICommand>();
    //    mcb
    //        .Setup(executeExpression)
    //        .Returns(Task.FromResult(0))
    //        .Verifiable();

    //    var mfb = new Mock<IFacade>();

    //    if (mockedFacadeMethod is not null)
    //    {
    //        mfb.Setup(mockedFacadeMethod)
    //            .Returns(mcb.Object)
    //            .Verifiable();
    //    }

    //    var mf = mfb.Object;

    //    //Environment.SetEnvironmentVariable(ConsoleHostedService.CommandLineEnvironmentVariableName, cmd);

    //    //Action<IConfigurationBuilder> bla = (b) =>
    //    //{
    //    //    b.AddInMemoryCollection(new Dictionary<string, string> {
    //    //            { "TempDir:TempDirectoryName", ".ariustemp2" }
    //    //        });
    //    //};

    //    var p = new Program();
    //    await p.Main(args.Split(' '), facade: mf);
    //    mcb.Verify(executeExpression, Times.Exactly(1));

    //    return (mfb, p.InvocationContext);
    //}
}