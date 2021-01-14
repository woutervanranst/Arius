using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks;
using Arius.CommandLine;
using Arius.Extensions;
using Arius.Models;
using Arius.Repositories;
using Arius.Services;
using Azure.Storage.Blobs;
using Microsoft.Azure.Cosmos.Table;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Arius.Repositories
{
    internal partial class AzureRepository
    {
        private class ManifestRepository
        {
            public ManifestRepository(ICommandExecutorOptions options, ILogger<ManifestRepository> logger)
            {
                _logger = logger;

                var o = (IAzureRepositoryOptions) options;

                var connectionString = $"DefaultEndpointsProtocol=https;AccountName={o.AccountName};AccountKey={o.AccountKey};EndpointSuffix=core.windows.net";

                var csa = CloudStorageAccount.Parse(connectionString);
                var tc = csa.CreateCloudTableClient();
                _manifestTable = tc.GetTableReference(o.Container + "manifests");

                var r = _manifestTable.CreateIfNotExists();
                if (r)
                    _logger.LogInformation($"Created manifestTable for {o.Container}... ");
            }

            private readonly ILogger<ManifestRepository> _logger;
            private readonly CloudTable _manifestTable;

            

            public async Task AddManifestAsync(BinaryFile f)
            {
                try
                {
                    var me = new ManifestEntry(f);

                    var op = TableOperation.Insert(me); //TO CHECK zitten alle Chunks hierin of enkel de geuploade? to test: delete 1 chunk remote en run opnieuw

                    await _manifestTable.ExecuteAsync(op);
                }
                catch (StorageException)
                {
                    //Console.WriteLine(e.Message);
                    //Console.ReadLine();
                    throw;
                }
            }

            public IEnumerable<HashValue> GetAllManifestHashes()
            {
                var query = _manifestTable.CreateQuery<TableEntity>()
                    .Select(e => new HashValue() {Value = e.PartitionKey});

                return query.AsEnumerable();
            }











        }

        
        public class ManifestEntry : TableEntity
        {
            public ManifestEntry(BinaryFile bf)
            {
                PartitionKey = bf.ManifestHash!.Value.Value;
                RowKey = bf.ManifestHash!.Value.Value;

                Chunks = JsonSerializer.Serialize(bf.Chunks.Select(cf => cf.Hash.Value));
            }



            public string Chunks { get; set; }
        }



        
        

        

    }
}