﻿using Arius.Cli;
using Arius.Core.Commands;
using Arius.Core.Facade;
using Moq;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.CommandLine.Invocation;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace Arius.Tests.Arius.Cli
{
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
            var accountName = "ha";
            var accountKey = "ha";
            var container = "h";
            var passphrase = "3";
            var synchronize = false;
            var download = false;
            var keepPointers = false;
            var path = "he";

            var cmd = "archive " +
                $"-n {accountName} " +
                $"-k {accountKey} " +
                $"-p {passphrase} " +
                $"-c {container} " +
                $"{(synchronize ? "--synchronize " : "")}" +
                $"{(download ? "--download " : "")}" +
                $"{(keepPointers ? "--keep-pointers " : "")}" +
                $"{path}";

            Expression<Func<IFacade, Core.Commands.ICommand>> expr = (m) => m.CreateRestoreCommand(accountName, accountKey, container, passphrase, synchronize, download, keepPointers, path);

            var mfb = await ExecuteMockedFacade(cmd, expr);

            Assert.AreEqual(0, Environment.ExitCode);

            mfb.Verify(expr, Times.Exactly(1));
        }

        [Test]
        public async Task Cli_RestoreCommandWithParameters_FacadeCalled()
        {
            var accountName = "ha";
            var accountKey = "ha";
            var container = "h";
            var passphrase = "3";
            var synchronize = false;
            var download = false;
            var keepPointers = false;
            var path = "he";

            var cmd = "restore " +
                $"-n {accountName} " +
                $"-k {accountKey} " +
                $"-p {passphrase} " +
                $"-c {container} " +
                $"{(synchronize ? "--synchronize " : "")}" +
                $"{(download ? "--download " : "")}" +
                $"{(keepPointers ? "--keep-pointers " : "")}" +
                $"{path}";

            Expression<Func<IFacade, Core.Commands.ICommand>> expr = (m) => m.CreateRestoreCommand(accountName, accountKey, container, passphrase, synchronize, download, keepPointers, path);

            var mfb = await ExecuteMockedFacade(cmd, expr);

            Assert.AreEqual(0, Environment.ExitCode);

            mfb.Verify(expr, Times.Exactly(1));
            

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


        private static async Task<Mock<IFacade>> ExecuteMockedFacade(string args, Expression<Func<IFacade, Core.Commands.ICommand>> mockedFacadeMethod = null)
        {
            var mcb = new Mock<ICommand>();
            mcb
                .Setup(m => m.Execute())
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

            return mfb;
        }
    }
}