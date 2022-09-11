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

    class ScenarioContextIds
    {
        public const string INITIAL = "InitialRepoStats";
        public const string AFTERARCHIVE = "AfterArchiveRepoStats";
    }
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


        public RemoteRepositorySteps(ScenarioContext sc, BlobContainerClient bcc, Repository r) //IObjectContainer oc, BlobContainerClient bcc, Facade f, ScenarioContext context)
        {
            this.scenarioContext = sc;
            this.container = bcc;
            this.repository = r;
        }

        private readonly ScenarioContext scenarioContext;
        private readonly BlobContainerClient container;
        private readonly Repository repository;

        [BeforeScenario]
        public async Task InitRepostats(ScenarioContext sc)
        {
            sc[ScenarioContextIds.INITIAL] = await GetRepoStats(repository);
        }

        [Given(@"a remote archive")]
        public void GivenRemoteArchive()
        {
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
        public async Task ThenAdditionalChunksUploaded(int p0)
        {
            var (chunkBlobItemCount0, _, _, _, _) = (RepoStat)scenarioContext[ScenarioContextIds.INITIAL];
            var (chunkBlobItemCount1, _, _, _, _) = (RepoStat)scenarioContext[ScenarioContextIds.AFTERARCHIVE];

            Assert.AreEqual(chunkBlobItemCount0 + p0, chunkBlobItemCount1);
        }


        public record RepoStat(int chunkBlobItemCount, 
            int binaryCount,
            PointerFileEntry[] currentPfeWithDeleted, 
            PointerFileEntry[] currentPfeWithoutDeleted, 
            PointerFileEntry[] allPfes);

        public static async Task<RepoStat> GetRepoStats(Repository repo)
        {
            var t1 = Task.Run(async () => await repo.Chunks.GetAllChunkBlobs().CountAsync());
            var t2 = Task.Run(async () => await repo.Binaries.CountAsync());
            var t3 = Task.Run(async () => (await repo.PointerFileEntries.GetCurrentEntriesAsync(true)).ToArray());
            var t4 = Task.Run(async () => (await repo.PointerFileEntries.GetCurrentEntriesAsync(false)).ToArray());
            var t5 = Task.Run(async () => (await repo.PointerFileEntries.GetPointerFileEntriesAsync()).ToArray());

            await Task.WhenAll(t1, t2, t3, t4, t5);

            return new(t1.Result, t2.Result, t3.Result, t4.Result, t5.Result);


            //var chunkBlobItemCount = repo.Chunks.GetAllChunkBlobs().CountAsync().Result;
            //var binaryCount = repo.Binaries.CountAsync().Result;

            //var currentPfeWithDeleted = repo.PointerFileEntries.GetCurrentEntriesAsync(true).Result.ToArray();
            //var currentPfeWithoutDeleted = repo.PointerFileEntries.GetCurrentEntriesAsync(false).Result.ToArray();

            //var allPfes = repo.PointerFileEntries.GetPointerFileEntriesAsync().Result.ToArray();

            //return (chunkBlobItemCount, binaryCount, currentPfeWithDeleted, currentPfeWithoutDeleted, allPfes);
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
