using Arius.Core.Commands;
using Arius.Core.Configuration;
using Arius.Core.Models;
using Arius.Core.Repositories;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Specialized;
using BoDi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using System;
using System.ComponentModel;
using TechTalk.SpecFlow;

namespace Arius.Core.BehaviorTests.StepDefinitions
{
    public record RepositoryOptions(string AccountName, string AccountKey, string Container, string Passphrase) : IRepositoryOptions;

    [Binding]
    class RemoteRepositorySteps
    {
        private const string TestContainerNamePrefix = "unittest";

        [BeforeTestRun(Order = 1)]
        public static async Task InitializeRemoteRepository(/*ITestRunnerManager testRunnerManager, ITestRunner testRunner, */IObjectContainer oc)
        {

            var accountName = Environment.GetEnvironmentVariable("ARIUS_ACCOUNT_NAME");
            if (string.IsNullOrEmpty(accountName))
                throw new ArgumentException("Environment variable ARIUS_ACCOUNT_NAME not specified");

            var accountKey = Environment.GetEnvironmentVariable("ARIUS_ACCOUNT_KEY");
            if (string.IsNullOrEmpty(accountKey))
                throw new ArgumentException("Environment variable ARIUS_ACCOUNT_KEY not specified");

            const string Passphrase = "myPassphrase";

            var containerName = $"{TestContainerNamePrefix}{DateTime.Now:yyMMdd-HHmmss}";


            var connectionString = $"DefaultEndpointsProtocol=https;AccountName={accountName};AccountKey={accountKey};EndpointSuffix=core.windows.net";
            var blobService = new BlobServiceClient(connectionString);
            var container = await blobService.CreateBlobContainerAsync(containerName);

            oc.RegisterInstanceAs<BlobContainerClient>(container);
            oc.RegisterInstanceAs(new RepositoryOptions(accountName, accountKey, containerName, Passphrase));

            oc.RegisterFactoryAs<IServiceProvider>((oc) =>
            {
                var options = oc.Resolve<RepositoryOptions>();
                var sp = ExecutionServiceProvider<RepositoryOptions>.BuildServiceProvider(NullLoggerFactory.Instance, options);

                return sp.Services;
            });
            oc.RegisterFactoryAs<Repository>((oc) =>
            {
                return oc.Resolve<IServiceProvider>().GetRequiredService<Repository>();
            });
        }

        



        

        public RemoteRepositorySteps(BlobContainerClient bcc, Repository r) //IObjectContainer oc, BlobContainerClient bcc, Facade f, ScenarioContext context)
        {
            this.container = bcc;
            this.repository = r;
        }

        private readonly BlobContainerClient container;
        private readonly Repository repository;
        private readonly ScenarioContext context;


        [Given(@"a remote archive")]
        public void GivenRemoteArchive()
        {
            RepoStats(out _, out _, out _, out _, out _, out _);
        }

        [Given(@"an empty remote archive")]
        public async Task GivenAnEmptyRemoteArchive()
        {
            await PurgeRemote(container);
        }
        public static async Task PurgeRemote(BlobContainerClient bcc)
        {
            // delete all blobs in the container but leave the container
            await foreach (var bi in bcc.GetBlobsAsync())
                await bcc.DeleteBlobAsync(bi.Name);
        }

        [Then(@"(.*) additional chunks uploaded")]
        public void ThenAdditionalChunksUploaded(int p0)
        {
            RepoStats(out _, out _, out _, out _, out _, out _);

            throw new PendingStepException();
        }




        protected void RepoStats(out Repository repo,
            out int chunkBlobItemCount,
            out int binaryCount,
            out PointerFileEntry[] currentPfeWithDeleted, out PointerFileEntry[] currentPfeWithoutDeleted,
            out PointerFileEntry[] allPfes)
        {
            repo = repository;

            chunkBlobItemCount = repo.Chunks.GetAllChunkBlobs().CountAsync().Result;
            binaryCount = repo.Binaries.CountAsync().Result;

            currentPfeWithDeleted = repo.PointerFileEntries.GetCurrentEntriesAsync(true).Result.ToArray();
            currentPfeWithoutDeleted = repo.PointerFileEntries.GetCurrentEntriesAsync(false).Result.ToArray();

            allPfes = repo.PointerFileEntries.GetPointerFileEntriesAsync().Result.ToArray();
        }






        [AfterTestRun]
        public static async Task OneTimeTearDown(BlobContainerClient container)
        {
            var blobService = container.GetParentBlobServiceClient();

            // Delete blobs
            foreach (var bci in blobService.GetBlobContainers(prefix: TestContainerNamePrefix))
                await blobService.GetBlobContainerClient(bci.Name).DeleteAsync();
        }
    }
}
