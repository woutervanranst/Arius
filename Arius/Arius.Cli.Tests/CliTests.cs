using Arius.Cli;
using Arius.Core.Commands;
using Moq;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.CommandLine.Invocation;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace Arius.Cli.Tests;

class CliTests
{
    [Test]
    public async Task Cli_NoCommand_ParseErrorResult()
    {
        var args = "";

        var p = new Program();
        await p.Main(args.Split(' '));

        Assert.IsInstanceOf<ParseErrorResult>(p.InvocationContext.InvocationResult);
        Assert.AreEqual(1, p.InvocationContext.ParseResult.Errors.Count(pe => pe.Message == "Required command was not provided."));
        Assert.AreEqual((int)Program.ExitCode.ERROR, Environment.ExitCode);
    }


    [Test]
    public async Task Cli_ArchiveCommandWithoutParameters_ParseErrorResult()
    {
        var args = "archive";

        var p = new Program();
        await p.Main(args.Split(' '));

        Assert.IsInstanceOf<ParseErrorResult>(p.InvocationContext.InvocationResult);
        Assert.AreEqual(1, p.InvocationContext.ParseResult.Errors.Count(pe => pe.Message == "Required argument missing for command: archive"));
        Assert.AreEqual((int)Program.ExitCode.ERROR, Environment.ExitCode);
    }

    [Test]
    public async Task Cli_RestoreCommandWithoutParameters_ParseErrorResult()
    {
        var args = "restore";

        var p = new Program();
        await p.Main(args.Split(' '));

        Assert.IsInstanceOf<ParseErrorResult>(p.InvocationContext.InvocationResult);
        Assert.AreEqual(1, p.InvocationContext.ParseResult.Errors.Count(pe => pe.Message == "Required argument missing for command: restore"));
        Assert.AreEqual((int)Program.ExitCode.ERROR, Environment.ExitCode);
    }


    [Test]
    public async Task Cli_NonExistingCommand_ParseErrorResult()
    {
        var args = "unexistingcommand";

        var p = new Program();
        await p.Main(args.Split(' '));

        Assert.IsInstanceOf<ParseErrorResult>(p.InvocationContext.InvocationResult);
        Assert.AreEqual(1, p.InvocationContext.ParseResult.Errors.Count(pe => pe.Message == $"Unrecognized command or argument '{args}'"));
        Assert.AreEqual((int)Program.ExitCode.ERROR, Environment.ExitCode);
    }


    [Test]
    public async Task Cli_ArchiveCommandWithParameters_FacadeCalled()
    {
        CreateArchiveCommand(out string accountName, out string accountKey, out string passphrase, out bool fastHash, out string container, out bool removeLocal, out string tier, out bool dedup, out string path, out string cmd);

        Expression<Func<IFacade, Core.Commands.ICommand>> expr = (m) => m.CreateArchiveCommand(accountName, accountKey, passphrase, fastHash, container, removeLocal, tier, dedup, path, DateTime.UtcNow);

        var r = await ExecuteMainWithMockedFacade(cmd, expr);

        Assert.AreEqual(0, Environment.ExitCode);

        r.MockFacade.Verify(expr, Times.Exactly(1));
        r.MockFacade.VerifyNoOtherCalls();
    }

    [Test]
    public async Task Cli_RestoreCommandWithParameters_FacadeCalled()
    {
        CreateRestoreCommand(out string accountName, out string accountKey, out string container, out string passphrase, out bool synchronize, out bool download, out bool keepPointers, out string path, out string cmd);

        Expression<Func<IFacade, Core.Commands.ICommand>> expr = (m) => m.CreateRestoreCommand(accountName, accountKey, container, passphrase, synchronize, download, keepPointers, path, DateTime.UtcNow);

        var r = await ExecuteMainWithMockedFacade(cmd, expr);

        Assert.AreEqual(0, Environment.ExitCode);

        r.MockFacade.Verify(expr, Times.Exactly(1));
        r.MockFacade.VerifyNoOtherCalls();


        // TODO Directory.Exists
        //var w = r.InvocationContext.InvocationResult;
        // TODO Facade throws error  
        // todo restore with file and DirectoryInfo

        //var macb = new Mock<Facade.ArchiveCommandBuilder>();
        //macb.Setup(m => m.ForStorageAccount(accountName, accountKey)).Returns(macb.Object);
        //macb.Setup(m => m.ForContainer(container)).Returns(macb.Object);
        //mfb.Setup(m => m.GetArchiveCommandBuilder()).Returns(macb.Object);



        //var x = mf.GetArchiveCommandBuilder()
        //    .ForStorageAccount(accountName, "h");


        //var myViewModel = TheOutletViewModelForTesting();
        //var mockBuilder = new Mock<IOutletViewModelBuilder>();

        //mockBuilder.Setup(m => m.WithOutlet(It.IsAny<int>())).Returns(mockBuilder.Object);
        //mockBuilder.Setup(m => m.WithCountryList()).Returns(mockBuilder.Object);
        //mockBuilder.Setup(m => m.Build()).Returns(myViewModel);


        //Facade f = m;

        //m.Setup(f => f.CreateArchiveCommand)


        /*
         * 
         * 
         * private async Task<IServiceProvider> ArchiveCommand(AccessTier tier, bool removeLocal = false, bool fastHash = false, bool dedup = false)
    {
        var cmd = "archive " +
            $"-n {TestSetup.AccountName} " +
            $"-k {TestSetup.AccountKey} " +
            $"-p {TestSetup.passphrase} " +
            $"-c {TestSetup.container.Name} " +
            $"{(removeLocal ? "--remove-local " : "")}" +
            $"--tier {tier.ToString().ToLower()} " +
            $"{(dedup ? "--dedup " : "")}" +
            $"{(fastHash ? "--fasthash" : "")}" +
            $"{TestSetup.archiveTestDirectory.FullName}";

        return await ExecuteCommand(cmd);   
    }

    private async Task<IServiceProvider> ExecuteCommand(string cmd)
    {
        Environment.SetEnvironmentVariable(Arius.AriusCommandService.CommandLineEnvironmentVariableName, cmd);

        //Action<IConfigurationBuilder> bla = (b) =>
        //{
        //    b.AddInMemoryCollection(new Dictionary<string, string> {
        //            { "TempDir:TempDirectoryName", ".ariustemp2" }
        //        });
        //};

        await Arius.Program.CreateHostBuilder(cmd.Split(' '), null).RunConsoleAsync();

        if (Environment.ExitCode != 0)
            throw new ApplicationException("Exitcode is not 0");

        var sp = TestSetup.GetServiceProvider();
        return sp;
    }
         * 
         */
    }

    private static void CreateRestoreCommand(out string accountName, out string accountKey, out string container, out string passphrase, out bool synchronize, out bool download, out bool keepPointers, out string path, out string cmd)
    {
        accountName = "ha";
        accountKey = "ha";
        container = "h";
        passphrase = "3";
        synchronize = false;
        download = false;
        keepPointers = false;
        path = "he";

        cmd = "restore " +
              $"-n {accountName} " +
              $"-k {accountKey} " +
              $"-p {passphrase} " +
              $"-c {container} " +
              $"{(synchronize ? "--synchronize " : "")}" +
              $"{(download ? "--download " : "")}" +
              $"{(keepPointers ? "--keep-pointers " : "")}" +
              $"{path}";
    }

    private static void CreateArchiveCommand(out string accountName, out string accountKey, out string passphrase, out bool fastHash, out string container, out bool removeLocal, out string tier, out bool dedup, out string path, out string cmd)
    {
        accountName = "ha";
        accountKey = "ha";
        passphrase = "3";
        container = "h";
        fastHash = false;
        removeLocal = false;
        tier = "cool";
        dedup = false;
        path = "he";

        cmd = "archive " +
              $"-n {accountName} " +
              $"-k {accountKey} " +
              $"-p {passphrase} " +
              $"-c {container} " +
              $"{(removeLocal ? "--remove-local " : "")}" +
              $"--tier {tier.ToString().ToLower()} " +
              $"{(dedup ? "--dedup " : "")}" +
              $"{(fastHash ? "--fasthash" : "")}" +
              $"{path}";
    }


    private static async Task<(Mock<IFacade> MockFacade, InvocationContext InvocationContext)> ExecuteMainWithMockedFacade(string args, Expression<Func<IFacade, Core.Commands.ICommand>> mockedFacadeMethod = null)
    {
        Expression<Func<ICommand, Task<int>>> executeExpression = (c) => c.Execute();
        var mcb = new Mock<ICommand>();
        mcb
            .Setup(executeExpression)
            .Returns(Task.FromResult(0))
            .Verifiable();

        var mfb = new Mock<IFacade>();

        if (mockedFacadeMethod is not null)
        {
            mfb.Setup(mockedFacadeMethod)
                .Returns(mcb.Object)
                .Verifiable();
        }

        var mf = mfb.Object;

        //Environment.SetEnvironmentVariable(ConsoleHostedService.CommandLineEnvironmentVariableName, cmd);

        //Action<IConfigurationBuilder> bla = (b) =>
        //{
        //    b.AddInMemoryCollection(new Dictionary<string, string> {
        //            { "TempDir:TempDirectoryName", ".ariustemp2" }
        //        });
        //};

        var p = new Program();
        await p.Main(args.Split(' '), facade: mf);
        mcb.Verify(executeExpression, Times.Exactly(1));

        return (mfb, p.InvocationContext);
    }
}