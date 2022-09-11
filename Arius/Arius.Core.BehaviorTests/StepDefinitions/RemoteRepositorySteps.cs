using Arius.Core.Commands;
using Arius.Core.Configuration;
using Azure.Storage.Blobs;
using BoDi;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using System;
using System.ComponentModel;
using TechTalk.SpecFlow;

namespace Arius.Core.BehaviorTests.StepDefinitions
{

    [Binding]
    public class RemoteRepositorySteps
    {

        public const string Passphrase = "myPassphrase";
        private const string TestContainerNamePrefix = "unittest";



        [BeforeTestRun(Order = 1)]
        public static async Task InitializeRemoteRepository(/*ITestRunnerManager testRunnerManager, ITestRunner testRunner, */IObjectContainer oc)
        {
            var containerName = $"{TestContainerNamePrefix}{DateTime.Now:yyMMddHHmmss}";

            var accountName = Environment.GetEnvironmentVariable("ARIUS_ACCOUNT_NAME");
            if (string.IsNullOrEmpty(accountName))
                throw new ArgumentException("Environment variable ARIUS_ACCOUNT_NAME not specified");

            var accountKey = Environment.GetEnvironmentVariable("ARIUS_ACCOUNT_KEY");
            if (string.IsNullOrEmpty(accountKey))
                throw new ArgumentException("Environment variable ARIUS_ACCOUNT_KEY not specified");

            var connectionString = $"DefaultEndpointsProtocol=https;AccountName={accountName};AccountKey={accountKey};EndpointSuffix=core.windows.net";
            var blobService = new BlobServiceClient(connectionString);
            var container = await blobService.CreateBlobContainerAsync(containerName);

            oc.RegisterInstanceAs<BlobContainerClient>(container);
        }

        [BeforeTestRun]
        public static void InitializeFacade(IObjectContainer oc)
        {
            oc.RegisterFactoryAs<Facade>((_) =>
            {
                var loggerFactory = LoggerFactory.Create(builder => builder.AddDebug());

                var tempDirectoryAppSettings = Options.Create(new TempDirectoryAppSettings()
                {
                    TempDirectoryName = ".ariustemp",
                    RestoreTempDirectoryName = ".ariusrestore"
                });

                return new Facade(loggerFactory, tempDirectoryAppSettings);
            });
        }



        

        public RemoteRepositorySteps(BlobContainerClient bcc) //IObjectContainer oc, BlobContainerClient bcc, Facade f, ScenarioContext context)
        {
            this.bcc = bcc;
        }

        private readonly BlobContainerClient bcc;
        private readonly ScenarioContext context;


        [Given(@"a remote archive")]
        public void GivenRemoteArchive()
        {
        }

        [Given(@"an empty remote archive")]
        public async Task GivenAnEmptyRemoteArchive()
        {
            // delete all blobs in the container but leave the container
            await foreach (var bi in bcc.GetBlobsAsync())
                await bcc.DeleteBlobAsync(bi.Name);
        }







        //[Given(@"an existing local archive with one file")]
        //public void GivenAnExistingLocalArchiveWithOneFile()
        //{
        //    throw new PendingStepException();
        //}

        //[When(@"I archive")]
        //public void WhenIArchive()
        //{
        //    throw new PendingStepException();
        //}

        //[Then(@"the files should be archived")]
        //public void ThenTheFilesShouldBeArchived()
        //{
        //    throw new PendingStepException();
        //}
    }
}
