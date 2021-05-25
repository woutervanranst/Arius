
using Arius.Core.Facade;
using Moq;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
namespace Arius.Tests.Arius.Cli
{
    class CliTests
    {
        [Test]
        public void Ha()
        {
            var accountName = "ha";
            var accountKey = "ha";
            var container = "h";

            var mfb = new Mock<Facade>();
            var macb = new Mock<Facade.ArchiveCommandBuilder>();

            macb.Setup(m => m.ForStorageAccount(accountName, accountKey)).Returns(macb.Object);
            macb.Setup(m => m.ForContainer(container)).Returns(macb.Object);


            mfb.Setup(m => m.GetArchiveCommandBuilder()).Returns(macb.Object);

            var mf = mfb.Object;


            var x = mf.GetArchiveCommandBuilder()
                .ForStorageAccount(accountName, "h");

            Assert.IsTrue(true);




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
    }
}