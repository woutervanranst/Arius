using Arius.Core.Commands;
using Arius.Core.Repositories;
using Arius.Core.Services;
using Azure.Storage.Blobs;
using BoDi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Text.RegularExpressions;
using TechTalk.SpecFlow;
using TechTalk.SpecFlow.Assist;

namespace Arius.Core.BehaviorTests2.StepDefinitions
{


    [Binding]
    class RestoreStepDefinitions : TestBase
    {
        private const string TestContainerNamePrefix = "unittest";

        

        record RepositoryOptions(string AccountName, string AccountKey, string Container, string Passphrase) : IRepositoryOptions;


        [BeforeTestRun(Order = 1)]
        public static async Task InitializeRemoteRepository(IObjectContainer oc)
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

        [BeforeTestRun(Order = 2)] //run after the RemoteRepository is initialized, and the BlobContainerClient is available for DI
        public static void InitializeLocalRepository(IObjectContainer oc, BlobContainerClient bcc)
        {
            var root = new DirectoryInfo(Path.Combine(Path.GetTempPath(), "arius"));
            var runRoot = root.CreateSubdirectory(bcc.Name);
            var sourceDirectory = runRoot.CreateSubdirectory("source");
            var testDirectory = runRoot.CreateSubdirectory("test");

            oc.RegisterInstanceAs(new Directories(root, runRoot, sourceDirectory, testDirectory));
        }

        public RestoreStepDefinitions(ScenarioContext sc) : base(sc)
        {
        }

        [Given(@"the following local files are archived:")]
        public async Task Haha(Table table)
        {
            var files = table.CreateSet<FileTableEntry>().ToList();

            foreach (var f in files)
            {
                var sizeInBytes = f.Size switch
                {
                    "BELOW_ARCHIVE_TIER_LIMIT"
                        => 12 * 1024 + 1, // 12 KB
                    "ABOVE_ARCHIVE_TIER_LIMIT"
                        => 1024 * 1024 + 1, // Note: the file needs to be big enough (> 1 MB) to put into Archive storage (see ChunkBlobBase.SetAccessTierPerPolicyAsync)
                    _ when
                        // see https://stackoverflow.com/a/3513858
                        // see https://codereview.stackexchange.com/a/67506
                        int.TryParse(Regex.Match(f.Size, @"(?<size>\d*) KB").Groups["size"].Value, out var size0)
                        => size0,
                    _ =>
                        throw new ArgumentOutOfRangeException()
                };





            }


        }

        record FileTableEntry(string Id, string RelativeFileName, string Size);
    }
}
