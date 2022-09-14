using Arius.Core.Commands;
using Arius.Core.Configuration;
using Arius.Core.Models;
using Arius.Core.Repositories;
using Arius.Core.Services;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
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

        //[BeforeScenario]
        //public static async Task InitRepostats(ScenarioContext sc)
        //{
        //    await sc.AddRepoStatsAsync();
        //}

        [Given(@"a remote archive")]
        public async Task GivenRemoteArchive()
        {
            await scenarioContext.AddRemoteRepoStatsAsync();
        }

        [Given(@"an empty remote archive")]
        public async Task GivenAnEmptyRemoteArchive()
        {
            await PurgeRemote(container);
            await scenarioContext.AddRemoteRepoStatsAsync();
        }
        public static async Task PurgeRemote(BlobContainerClient bcc)
        {
            // delete all blobs in the container but leave the container
            await foreach (var bi in bcc.GetBlobsAsync())
                await bcc.DeleteBlobAsync(bi.Name);
        }

        [Then("{int} additional Chunk(s)")]
        public void ThenAdditionalChunks(int x)
        {
            var x0 = scenarioContext.GetRemoteRepoStats().SkipLast(1).Last().chunkBlobItemCount;
            var x1 = scenarioContext.GetRemoteRepoStats().Last().chunkBlobItemCount;

            Assert.AreEqual(x0 + x, x1);
        }

        [Then("{int} additional Manifest(s)")]
        public void ThenAdditionalManifests(int x)
        {
            var x0 = scenarioContext.GetRemoteRepoStats().SkipLast(1).Last().binaryCount;
            var x1 = scenarioContext.GetRemoteRepoStats().Last().binaryCount;

            Assert.AreEqual(x0 + x, x1);
        }

        [Then("{int} additional total PointerFileEntry/PointerFileEntries")]
        public void ThenAdditionalTotalPointerFileEntry(int x)
        {
            var x0 = scenarioContext.GetRemoteRepoStats().SkipLast(1).Last().allPfes.Length;
            var x1 = scenarioContext.GetRemoteRepoStats().Last().allPfes.Length;

            Assert.AreEqual(x0 + x, x1);
        }

        [Then(@"{int} additional existing PointerFileEntry/PointerFileEntries")]
        public void ThenAdditionalExistingPointerFileEntry(int x)
        {
            var x0 = scenarioContext.GetRemoteRepoStats().SkipLast(1).Last().currentPfeWithoutDeleted.Length;
            var x1 = scenarioContext.GetRemoteRepoStats().Last().currentPfeWithoutDeleted.Length;

            (x0 + x).Should().Be(x1);
        }

        [Then(@"all chunks are in the {word} tier")]
        public async Task ThenAllChunksAreInTheTier(string t)
        {
            var tier = (AccessTier)t;

            var chunks = await scenarioContext.GetRepository().Chunks.GetAllChunkBlobs().ToListAsync();

            chunks.Select(cbb => cbb.AccessTier).Should().Equal(tier);
        }

        [Then(@"{int} total existing PointerFileEntry/PointerFileEntries")]
        public void ThenTotalExistingPointerFileEntries(int x)
        {
            var x1 = scenarioContext.GetRemoteRepoStats().Last().currentPfeWithoutDeleted.Length;

            x1.Should().Be(x);
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
