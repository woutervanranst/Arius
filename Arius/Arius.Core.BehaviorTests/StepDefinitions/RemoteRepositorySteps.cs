using Arius.Core.Commands;
using Arius.Core.Configuration;
using Arius.Core.Models;
using Arius.Core.Repositories;
using Arius.Core.Services;
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
using static Arius.Core.BehaviorTests.StepDefinitions.RemoteRepositorySteps;
using static Arius.Core.BehaviorTests.StepDefinitions.ScenarioContextExtensions;

namespace Arius.Core.BehaviorTests.StepDefinitions
{
    public record RepositoryOptions(string AccountName, string AccountKey, string Container, string Passphrase) : IRepositoryOptions;

    [Binding]
    class TestBase
    {
        public TestBase(ScenarioContext sc)
        {
            scenarioContext = sc;
        }

        protected readonly ScenarioContext scenarioContext;
    }

    class LocalTestBase : TestBase
    {
        public LocalTestBase(ScenarioContext sc, Directories dirs) : base(sc)
        {
            directories = dirs;
        }

        protected readonly Directories directories;

        protected (PointerFile pf, PointerFileEntry? pfe) GetPointerInfo(FileInfo fi)
        {
            var pf = scenarioContext.GetPointerService().GetPointerFile(fi);

            var a_rn = Path.GetRelativePath(directories.ArchiveTestDirectory.FullName, fi.FullName);
            var pfe = scenarioContext.GetRepository().PointerFileEntries.GetCurrentEntriesAsync(includeDeleted: true).Result.SingleOrDefault(r => r.RelativeName.StartsWith(a_rn));

            return (pf, pfe);

        }
    }

    static class ScenarioContextExtensions
    {
        public static Repository GetRepository(this ScenarioContext sc) => sc.ScenarioContainer.Resolve<Repository>();
        public static PointerService GetPointerService(this ScenarioContext sc) => sc.ScenarioContainer.Resolve<PointerService>();

        public static async Task AddRepoStatsAsync(this ScenarioContext sc, ScenarioContextIds id) => sc[id.ToString()] = await GetRepoStatsAsync(GetRepository(sc));
        public static RepoStat GetRepoStat(this ScenarioContext sc, ScenarioContextIds id) => (RepoStat)sc[id.ToString()];


        public enum ScenarioContextIds
        {
            INITIAL,
            AFTERARCHIVE,
            FILE1
        }

        public record RepoStat(int chunkBlobItemCount,
            int binaryCount,
            PointerFileEntry[] currentPfeWithDeleted,
            PointerFileEntry[] currentPfeWithoutDeleted,
            PointerFileEntry[] allPfes);

        private static async Task<RepoStat> GetRepoStatsAsync(Repository repo)
        {
            var chunkBlobItemCount = await repo.Chunks.GetAllChunkBlobs().CountAsync();
            var binaryCount = await repo.Binaries.CountAsync();

            var currentPfeWithDeleted = (await repo.PointerFileEntries.GetCurrentEntriesAsync(true)).ToArray();
            var currentPfeWithoutDeleted = (await repo.PointerFileEntries.GetCurrentEntriesAsync(false)).ToArray();

            var allPfes = (await repo.PointerFileEntries.GetPointerFileEntriesAsync()).ToArray();

            return new RepoStat(chunkBlobItemCount, binaryCount, currentPfeWithDeleted, currentPfeWithoutDeleted, allPfes);
        }

    }
    
    [Binding]
    class RemoteRepositorySteps : TestBase
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
            }).InstancePerDependency(); // see https://github.com/SpecFlowOSS/BoDi/pull/16
            oc.RegisterFactoryAs<Repository>((oc) => oc.Resolve<IServiceProvider>().GetRequiredService<Repository>()).InstancePerDependency();
            oc.RegisterFactoryAs<PointerService>((oc) => oc.Resolve<IServiceProvider>().GetRequiredService<PointerService>()).InstancePerDependency();
        }


        public RemoteRepositorySteps(ScenarioContext sc, BlobContainerClient bcc) : base(sc) //IObjectContainer oc, BlobContainerClient bcc, Facade f, ScenarioContext context)
        {
            this.container = bcc;
        }

        private readonly BlobContainerClient container;

        [BeforeScenario]
        public static async Task InitRepostats(ScenarioContext sc)
        {
            await sc.AddRepoStatsAsync(ScenarioContextIds.INITIAL);
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

        [Then(@"(.*) additional chunks?")]
        public void ThenAdditionalChunks(int x)
        {
            var x0 = scenarioContext.GetRepoStat(ScenarioContextIds.INITIAL).chunkBlobItemCount;
            var x1 = scenarioContext.GetRepoStat(ScenarioContextIds.AFTERARCHIVE).chunkBlobItemCount;

            Assert.AreEqual(x0 + x, x1);
        }

        [Then(@"(.*) additional manifests?")]
        public void ThenAdditionalManifests(int x)
        {
            var x0 = scenarioContext.GetRepoStat(ScenarioContextIds.INITIAL).binaryCount;
            var x1 = scenarioContext.GetRepoStat(ScenarioContextIds.AFTERARCHIVE).binaryCount;

            Assert.AreEqual(x0 + x, x1);
        }

        [Then(@"(.*) additional PointerFileEntry?")]
        public void ThenAdditionalPointerFileEntry(int x)
        {
            var x0 = scenarioContext.GetRepoStat(ScenarioContextIds.INITIAL).currentPfeWithDeleted.Length;
            var x1 = scenarioContext.GetRepoStat(ScenarioContextIds.AFTERARCHIVE).currentPfeWithDeleted.Length;

            Assert.AreEqual(x0 + x, x1);
        }

        [Then(@"(.*) additional existing PointerFileEntry?")]
        public void ThenAdditionalExistingPointerFileEntry(int x)
        {
            var x0 = scenarioContext.GetRepoStat(ScenarioContextIds.INITIAL).currentPfeWithoutDeleted.Length;
            var x1 = scenarioContext.GetRepoStat(ScenarioContextIds.AFTERARCHIVE).currentPfeWithoutDeleted.Length;

            Assert.AreEqual(x0 + x, x1);
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
