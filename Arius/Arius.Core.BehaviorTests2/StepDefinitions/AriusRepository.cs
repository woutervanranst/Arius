using Arius.Core.Commands;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Specialized;
using Microsoft.Extensions.Logging.Abstractions;

namespace Arius.Core.BehaviorTests2.StepDefinitions
{
    [Binding]
    static class AriusRepository
    {
        private const string TestContainerNamePrefix = "unittest";
        private record RepositoryOptions(string AccountName, string AccountKey, string Container, string Passphrase) : IRepositoryOptions;

        [BeforeTestRun(Order = 1)]
        private static async Task ClassInit()
        {
            var accountName = Environment.GetEnvironmentVariable("ARIUS_ACCOUNT_NAME");
            if (string.IsNullOrEmpty(accountName))
                throw new ArgumentException("Environment variable ARIUS_ACCOUNT_NAME not specified");

            var accountKey = Environment.GetEnvironmentVariable("ARIUS_ACCOUNT_KEY");
            if (string.IsNullOrEmpty(accountKey))
                throw new ArgumentException("Environment variable ARIUS_ACCOUNT_KEY not specified");

            const string passphrase = "myPassphrase";

            var containerName = $"{TestContainerNamePrefix}{DateTime.Now:yyMMdd-HHmmss}";

            var connectionString = $"DefaultEndpointsProtocol=https;AccountName={accountName};AccountKey={accountKey};EndpointSuffix=core.windows.net";
            var blobService = new BlobServiceClient(connectionString);
            Container = await blobService.CreateBlobContainerAsync(containerName);

            options = new RepositoryOptions(accountName, accountKey, Container.Name, passphrase);
            serviceProvider = ExecutionServiceProvider<RepositoryOptions>.BuildServiceProvider(NullLoggerFactory.Instance, options).Services;

            //oc.RegisterFactoryAs<Repository>((oc) => oc.Resolve<IServiceProvider>().GetRequiredService<Repository>()).InstancePerDependency();
            //oc.RegisterFactoryAs<PointerService>((oc) => oc.Resolve<IServiceProvider>().GetRequiredService<PointerService>()).InstancePerDependency();
        }

        private static IServiceProvider serviceProvider;
        private static RepositoryOptions options;
        internal static BlobContainerClient Container { get; private set; }

        [AfterTestRun]
        public static async Task ClassCleanup()
        {
            var blobService = Container.GetParentBlobServiceClient();

            // Delete blobs
            foreach (var bci in blobService.GetBlobContainers(prefix: TestContainerNamePrefix))
                await blobService.GetBlobContainerClient(bci.Name).DeleteAsync();
        }



    }
}