using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Arius.CommandLine;
using Arius.Models;
using Arius.Services;
using Microsoft.Azure.Cosmos.Table;
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

            public async Task AddManifestAsync(BinaryFile bf)
            {
                try
                {
                    var dto = new ManifestEntry(bf).GetDto();

                    var op = TableOperation.Insert(dto); //TO CHECK zitten alle Chunks hierin of enkel de geuploade? to test: delete 1 chunk remote en run opnieuw

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

            public IEnumerable<HashValue> GetChunkHashes(HashValue manifestHash)
            {
                var dto = _manifestTable.CreateQuery<ManifestEntryDto>()
                    .Where(dto2 =>
                        dto2.PartitionKey == manifestHash.Value &&
                        dto2.RowKey == manifestHash.Value) // LINQ provider does not support Single() natively
                    .AsEnumerable().Single();

                return new ManifestEntry(dto).Chunks;
            }


            private class ManifestEntry
            {
                private readonly ManifestEntryDto _dto;

                public ManifestEntry(BinaryFile bf)
                {
                    _dto = new ManifestEntryDto()
                    {
                        PartitionKey = bf.Hash.Value,
                        RowKey = bf.Hash.Value,

                        Chunks = JsonSerializer.Serialize(bf.Chunks.Select(cf => cf.Hash.Value))
                    };
                }
                public ManifestEntry(ManifestEntryDto dto)
                {
                    _dto = dto;
                }

                public ManifestEntryDto GetDto()
                {
                    return _dto;
                }

                public IEnumerable<HashValue> Chunks
                {
                    get
                    {
                        return JsonSerializer.Deserialize<IEnumerable<string>>(_dto.Chunks)?.Select(hv => new HashValue() {Value = hv});
                    }
                }
            }

            private class ManifestEntryDto : TableEntity
            {
                public string Chunks { get; init; }
            }
        }
    }
}