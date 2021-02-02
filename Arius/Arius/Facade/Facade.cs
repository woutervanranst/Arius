using Arius.CommandLine;
using Arius.Models;
using Arius.Repositories;
using Azure.Storage.Blobs;
using Microsoft.Azure.Cosmos.Table;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace Arius.Facade
{
    public class Facade
    {
        public Facade(ILoggerFactory loggerFactory, ILogger<Facade> logger)
        {
            this.loggerFactory = loggerFactory;
            this.logger = logger;
        }

        private readonly ILoggerFactory loggerFactory;
        private readonly ILogger logger;

        public IEnumerable<string> GetAzureRepositoryContainerNames(string accountName, string accountKey)
        {
            try
            { 
                var connectionString = $"DefaultEndpointsProtocol=https;AccountName={accountName};AccountKey={accountKey};EndpointSuffix=core.windows.net";

                var blobServiceClient = new BlobServiceClient(connectionString);

                var csa = CloudStorageAccount.Parse(connectionString);
                var tableClient = csa.CreateCloudTableClient();

                var tables = tableClient.ListTables().Select(ct => ct.Name).ToArray();

                var r = blobServiceClient.GetBlobContainers()
                    .Where(bci => tables.Contains($"{bci.Name}{AzureRepository.TableNameSuffix}"))
                    .Select(bci => bci.Name)
                    .ToArray();

                return r;
            }
            catch (Exception e) when (e is FormatException || e is StorageException)
            {
                throw new ArgumentException("Invalid combination of Account Name / Key", e);
            }
        }

        public async IAsyncEnumerable<IAriusEntry> GetLocalPathItems(DirectoryInfo di)
        {
            var block = new IndexDirectoryBlockProvider(loggerFactory.CreateLogger<IndexDirectoryBlockProvider>()).GetBlock();

            block.Post(di);
            block.Complete();

            while (await block.OutputAvailableAsync())
            {
                while (block.TryReceive(out var item))
                {
                    yield return item;
                }
            }

            await block.Completion.ConfigureAwait(false);
        }
    }
}
