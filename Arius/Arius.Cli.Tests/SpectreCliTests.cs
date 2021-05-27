//using Arius.Cli;
//using Arius.Core.Commands;
//using Arius.Core.Facade;
//using Moq;
//using NUnit.Framework;
//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Linq.Expressions;
//using System.Text;
//using System.Threading.Tasks;

//namespace Arius.Tests.Arius.Cli
//{
//    class CliTests
//    {
//        [Test]
//        public async Task Cli_AriusCommand_EnvOK()
//        {
//            var args = "";

//            var r = SpectreProgram.Main(args.Split(' '));

//            Assert.AreEqual(0, Environment.ExitCode);
//        }

//        [Test]
//        public async Task Cli_AriusArchiveCommand_CommandRuntimeException()
//        {
//            var args = "archive";

//            int r;

//            Assert.Catch<CommandRuntimeException>(() => r = SpectreProgram.Main(args.Split(' ')));
//            Assert.AreEqual(1, Environment.ExitCode);
//        }

//        [Test]
//        public async Task Cli_AriusRestoreCommand_CommandRuntimeException()
//        {
//            var args = "restore";

//            int r;

//            Assert.Catch<CommandRuntimeException>(() => r = SpectreProgram.Main(args.Split(' ')));
//            Assert.AreEqual(1, Environment.ExitCode);
//        }

//        [Test]
//        public async Task Cli_AriusUnexistingCommand_CommandRuntimeException()
//        {
//            var args = "unexistingcommand";

//            int r;

//            Assert.Catch<CommandRuntimeException>(() => r = SpectreProgram.Main(args.Split(' ')));
//            Assert.AreEqual(1, Environment.ExitCode);
//        }



//        [Test]
//        public async Task Main_RestoreCliCommand_SuccessfulExecution()
//        {
//            var accountName = "ha";
//            var accountKey = "ha";
//            var container = "h";
//            var passphrase = "3";
//            var synchronize = false;
//            var download = false;
//            var keepPointers = false;
//            var path = "he";

//            var cmd = "restore " +
//                $"-n {accountName} " +
//                $"-k {accountKey} " +
//                $"-p {passphrase} " +
//                $"-c {container} " +
//                $"{(synchronize ? "--synchronize " : "")}" +
//                $"{(download ? "--download " : "")}" +
//                $"{(keepPointers ? "--keep-pointers " : "")}" +
//                $"{path}";

//            Expression<Func<IFacade, Core.Commands.ICommand>> expr = (m) => m.CreateRestoreCommand(accountName, accountKey, container, passphrase, synchronize, download, keepPointers, path);

//            var mfb = await ExecuteMockedFacade(cmd, expr);

//            Assert.AreEqual(0, Environment.ExitCode);

//            mfb.Verify(expr, Times.Exactly(1));
            

//            //var macb = new Mock<Facade.ArchiveCommandBuilder>();
//            //macb.Setup(m => m.ForStorageAccount(accountName, accountKey)).Returns(macb.Object);
//            //macb.Setup(m => m.ForContainer(container)).Returns(macb.Object);
//            //mfb.Setup(m => m.GetArchiveCommandBuilder()).Returns(macb.Object);



//            //var x = mf.GetArchiveCommandBuilder()
//            //    .ForStorageAccount(accountName, "h");


//            //var myViewModel = TheOutletViewModelForTesting();
//            //var mockBuilder = new Mock<IOutletViewModelBuilder>();

//            //mockBuilder.Setup(m => m.WithOutlet(It.IsAny<int>())).Returns(mockBuilder.Object);
//            //mockBuilder.Setup(m => m.WithCountryList()).Returns(mockBuilder.Object);
//            //mockBuilder.Setup(m => m.Build()).Returns(myViewModel);


//            //Facade f = m;

//            //m.Setup(f => f.CreateArchiveCommand)


//            /*
//             * 
//             * 
//             * private async Task<IServiceProvider> ArchiveCommand(AccessTier tier, bool removeLocal = false, bool fastHash = false, bool dedup = false)
//        {
//            var cmd = "archive " +
//                $"-n {TestSetup.AccountName} " +
//                $"-k {TestSetup.AccountKey} " +
//                $"-p {TestSetup.passphrase} " +
//                $"-c {TestSetup.container.Name} " +
//                $"{(removeLocal ? "--remove-local " : "")}" +
//                $"--tier {tier.ToString().ToLower()} " +
//                $"{(dedup ? "--dedup " : "")}" +
//                $"{(fastHash ? "--fasthash" : "")}" +
//                $"{TestSetup.archiveTestDirectory.FullName}";

//            return await ExecuteCommand(cmd);   
//        }

//        private async Task<IServiceProvider> ExecuteCommand(string cmd)
//        {
//            Environment.SetEnvironmentVariable(Arius.AriusCommandService.CommandLineEnvironmentVariableName, cmd);

//            //Action<IConfigurationBuilder> bla = (b) =>
//            //{
//            //    b.AddInMemoryCollection(new Dictionary<string, string> {
//            //            { "TempDir:TempDirectoryName", ".ariustemp2" }
//            //        });
//            //};

//            await Arius.Program.CreateHostBuilder(cmd.Split(' '), null).RunConsoleAsync();

//            if (Environment.ExitCode != 0)
//                throw new ApplicationException("Exitcode is not 0");

//            var sp = TestSetup.GetServiceProvider();
//            return sp;
//        }
//             * 
//             */
//        }


//        private static async Task<Mock<IFacade>> ExecuteMockedFacade(string cmd, Expression<Func<IFacade, Core.Commands.ICommand>> mockedFacadeMethod = null)
//        {
//            throw new NotImplementedException();

//            //var mcb = new Mock<ICommand>();
//            //mcb
//            //    .Setup(m => m.Execute())
//            //    .Returns(Task.FromResult(0))
//            //    .Verifiable();

//            //var mfb = new Mock<IFacade>();

//            //if (mockedFacadeMethod is not null)
//            //{
//            //    mfb.Setup(mockedFacadeMethod)
//            //        .Returns(mcb.Object)
//            //        .Verifiable();
//            //}

//            //var mf = mfb.Object;

//            //Environment.SetEnvironmentVariable(ConsoleHostedService.CommandLineEnvironmentVariableName, cmd);

//            ////Action<IConfigurationBuilder> bla = (b) =>
//            ////{
//            ////    b.AddInMemoryCollection(new Dictionary<string, string> {
//            ////            { "TempDir:TempDirectoryName", ".ariustemp2" }
//            ////        });
//            ////};

//            //await Program.RunConsoleAync(cmd.Split(' '), facade: mf);

//            //return mfb;
//        }
//    }
//}