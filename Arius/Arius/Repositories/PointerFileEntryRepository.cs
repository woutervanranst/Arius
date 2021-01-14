using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Arius.CommandLine;
using Arius.Extensions;
using Arius.Models;
using Arius.Services;
using Microsoft.Azure.Cosmos.Table;
using Microsoft.Extensions.Logging;

namespace Arius.Repositories
{
    internal partial class AzureRepository
    {
        private class PointerFileEntryRepository
        {
            public PointerFileEntryRepository(ICommandExecutorOptions options, ILogger<PointerFileEntryRepository> logger)
            {
                _logger = logger;

                var o = (IAzureRepositoryOptions)options;

                var connectionString = $"DefaultEndpointsProtocol=https;AccountName={o.AccountName};AccountKey={o.AccountKey};EndpointSuffix=core.windows.net";

                var csa = CloudStorageAccount.Parse(connectionString);
                var tc = csa.CreateCloudTableClient();
                _pointerEntryTable = tc.GetTableReference(o.Container + "pointers");

                var r = _pointerEntryTable.CreateIfNotExists();
                if (r)
                    _logger.LogInformation($"Created tables for {o.Container}... ");
            }

            private readonly ILogger<PointerFileEntryRepository> _logger;
            private readonly CloudTable _pointerEntryTable;

            private static readonly PointerFileEntryEqualityComparer pfeec = new();


            

            public async Task CreatePointerFileEntryIfNotExistsAsync(PointerFile pointerFile, DateTime version)
            {
                var pfe = new PointerFileEntry(pointerFile, version)
                {
                    RelativeName = pointerFile.RelativeName,
                    Version = version,
                    CreationTimeUtc = File.GetCreationTimeUtc(pointerFile.FullName), //TODO
                    LastWriteTimeUtc = File.GetLastWriteTimeUtc(pointerFile.FullName),
                    IsDeleted = false
                };

                await CreatePointerFileEntryIfNotExistsAsync(pfe);
            }

            public async Task CreatePointerFileEntryIfNotExistsAsync(PointerFileEntry pfe, DateTime version, bool isDeleted = false)
            {
                if (isDeleted)
                {
                    pfe = new PointerFileEntry(pfe.Hash, pfe.RelativeName, version)
                    {
                        RelativeName = pfe.RelativeName,
                        Version = version,
                        IsDeleted = true,
                        CreationTimeUtc = null,
                        LastWriteTimeUtc = null
                    };

                    //m.Entries.Add(new PointerFileEntry()
                    //{
                    //    RelativeName = pfe.RelativeName,
                    //    Version = version,
                    //    IsDeleted = true,
                    //    CreationTimeUtc = null,
                    //    LastWriteTimeUtc = null
                    //});
                }
                else
                {
                    throw new NotImplementedException();
                }

                await CreatePointerFileEntryIfNotExistsAsync(pfe);
        }

            private async Task CreatePointerFileEntryIfNotExistsAsync(PointerFileEntry pfe)
            {
                try
                {
                    var pfes = GetAllEntries(pfe.RelativeName, pfe.Hash);

                    var xx = pfes.ToList(); //TODO DELETE

                    if (!pfes.Contains(pfe, pfeec))
                    {
                        var op = TableOperation.Insert(pfe);

                        await _pointerEntryTable.ExecuteAsync(op);

                        if (pfe.IsDeleted)
                            _logger.LogInformation($"Deleted {pfe.RelativeName}");
                        else
                            _logger.LogInformation($"Added {pfe.RelativeName}");
                    }
                }
                catch (StorageException)
                {
                    //Console.WriteLine(e.Message);
                    //Console.ReadLine();
                    throw;
                }
            }

            public TableQuery<PointerFileEntry> GetAllEntries()
            {
                var query = _pointerEntryTable.CreateQuery<PointerFileEntry>();

                throw new NotImplementedException();

                return query;
            }


            private IEnumerable<PointerFileEntry> GetAllEntries(string relativeName, HashValue hash)
            {
                var rnh = PointerFileEntry.GetRelativeNameHash(relativeName);

                var query = GetAllEntries()
                    .Where(pfe => pfe.PartitionKey == hash.Value &&
                                  String.Compare(pfe.RowKey, rnh, StringComparison.Ordinal) >= 0);

                return query;
            }

            public IEnumerable<PointerFileEntry> GetLastEntries(DateTime pointInTime, bool includeLastDeleted)
            {
                var r = GetAllEntries()
                    .GroupBy(pfe => pfe.RowKey.Substring(0, 8))
                    //.Select(g => g.OrderByDescending(pfe => pfe.RowKey).First());
                    .SelectMany(g => g.Take(1));

                //TODO KARL is this optimal?


                //TODO karl multithreading debugging

                /*
                 * https://docs.microsoft.com/en-us/azure/cosmos-db/table-storage-design-guide#solution-6
                 *  Table storage is lexicographically ordered ?
                 */
                //

                var xx = r.ToList();

                if (includeLastDeleted)
                    return r;
                else
                    return r.Where(e => !e.IsDeleted);
            }



            



        }

        public class PointerFileEntry : TableEntity
        {
            public static string GetRelativeNameHash(string relativeName)
            {
                var neutralRelativeName = relativeName
                    .ToLower(CultureInfo.InvariantCulture)
                    .Replace(Path.DirectorySeparatorChar, '/');

                return $"{Crc32Provider.Get(neutralRelativeName):x8}";
            }
            private static readonly Crc32 Crc32Provider = new();


            public PointerFileEntry()
            {
            }
            public PointerFileEntry(PointerFile pf, DateTime version) : this(pf.Hash, pf.RelativeName, version)
            {
            }
            public PointerFileEntry(HashValue hash, string relativeName, DateTime version)
            {
                //var xxx = $"{BitConverter.ToString(SHA256.HashData(pf.RelativeName.Select(c => Convert.ToByte(c)).ToArray())):x8}".ToLower().Replace("-","");

                PartitionKey = hash.Value;
                RowKey = $"{GetRelativeNameHash(relativeName)}-{version.Ticks *-1:x8}"; //Make ticks negative so when lexicographically sorting the RowKey the most recent version is on top
            }

            public string RelativeName { get; set; }
            public DateTime Version { get; set; }
            public bool IsDeleted { get; set; }
            public DateTime? CreationTimeUtc { get; set; }
            public DateTime? LastWriteTimeUtc { get; set; }

            [IgnoreProperty]
            public HashValue Hash => new() {Value = PartitionKey};
        }

    }
}
