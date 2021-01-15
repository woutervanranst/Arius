using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security;
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

                var o = (IAzureRepositoryOptions) options;

                _passphrase = o.Passphrase;

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
            private readonly string _passphrase;

            private static readonly PointerFileEntryEqualityComparer _pfeec = new();




            public async Task CreatePointerFileEntryIfNotExistsAsync(PointerFile pf, DateTime version)
            {
                var pfe = CreatePointerFileEntry(pf, version);

                await CreatePointerFileEntryIfNotExistsAsync(pfe);
            }

            public async Task CreatePointerFileEntryIfNotExistsAsync(PointerFileEntry2 pfe, DateTime version, bool isDeleted = false)
            {
                var pfe2 = CreatePointerFileEntry(pfe, version, isDeleted);

                await CreatePointerFileEntryIfNotExistsAsync(pfe2);
            }

            private async Task CreatePointerFileEntryIfNotExistsAsync(PointerFileEntry2 pfe)
            {
                try
                {
                    var dtos = GetAllEntries(pfe.RelativeName, pfe.ManifestHash);

                    var xx = dtos.ToList(); //TODO DELETE


                    // equivalent of   dtos.Contains(pfe2)  but based on hashcode since the relative names are encrypted
                    if (!dtos.AsEnumerable().Select(dto => dto.GetHashCode()).Contains(pfe.GetHashCode()))
                    {
                        var dto = CreatePointerFileEntryDto(pfe);
                        var op = TableOperation.Insert(dto);
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

            private TableQuery<PointerFileEntryDto> GetAllEntries()
            {
                var query = _pointerEntryTable.CreateQuery<PointerFileEntryDto>();
                return query;
            }



            private IQueryable<PointerFileEntryDto> GetAllEntries(string relativeName, HashValue manifestHash)
            {
                var rnh = GetRelativeNameHash(relativeName);

                var query = GetAllEntries()
                    .Where(dto => 
                        dto.PartitionKey == manifestHash.Value && 
                        string.Compare(dto.RowKey, rnh, StringComparison.Ordinal) >= 0);

                return query;
            }

            internal static string GetRelativeNameHash(string relativeName)
            {
                var neutralRelativeName = relativeName
                    .ToLower(CultureInfo.InvariantCulture)
                    .Replace(Path.DirectorySeparatorChar, '/');

                return $"{Crc32Provider.Get(neutralRelativeName):x8}";
            }
            private static readonly Crc32 Crc32Provider = new();






            public IEnumerable<PointerFileEntry2> GetLastEntries(DateTime pointInTime, bool includeLastDeleted)
            {
                //var r = GetAllEntries()
                //    .GroupBy(pfe => pfe.RowKey.Substring(0, 8))
                //    .Select(g => g.OrderByDescending(pfe => pfe.RowKey).First());
                //    //.SelectMany(g => g.Take(1));

                /* //TODO KARL is this optimal?
                 * https://docs.microsoft.com/en-us/azure/cosmos-db/table-storage-design-guide#solution-6
                 *  Table storage is lexicographically ordered ?
                 */


                //TODO karl multithreading debugging

                var r = GetAllEntries().AsEnumerable()
                    .GroupBy(pfe => pfe.RelativeNameHash)
                    .Select(g => g.OrderBy(pfe => pfe.Version).Last())
                    /*
                     *  Equivalent of
                     *      if (includeLastDeleted)
                     *          return r;
                     *      else
                     *          return r.Where(e => !e.IsDeleted);
                     */
                    .Where(dto => includeLastDeleted || !dto.IsDeleted)
                    .Select(dto => CreatePointerFileEntry(dto));

                var xx = r.ToList();

                return r;
            }




            private PointerFileEntry2 CreatePointerFileEntry(PointerFile pf, DateTime version)
            {
                return new()
                {
                    ManifestHash = pf.Hash,
                    RelativeName = pf.RelativeName,
                    Version = version,
                    IsDeleted = false,
                    CreationTimeUtc = File.GetCreationTimeUtc(pf.FullName), //TODO
                    LastWriteTimeUtc = File.GetLastWriteTimeUtc(pf.FullName),
                };
            }

            private PointerFileEntry2 CreatePointerFileEntry(PointerFileEntry2 pfe, DateTime version, bool isDeleted)
            {
                if (isDeleted)
                    return pfe with
                    {
                        Version = version,
                        IsDeleted = true,
                        CreationTimeUtc = null,
                        LastWriteTimeUtc = null
                    };
                else
                    throw new NotImplementedException();
            }
            private PointerFileEntry2 CreatePointerFileEntry(PointerFileEntryDto dto)
            {
                var rn = StringCipher.Decrypt(dto.EncryptedRelativeName, _passphrase);
                rn = ToPlatformSpecificPath(rn);

                return new()
                {
                    ManifestHash = new HashValue() { Value = dto.PartitionKey },
                    RelativeName = rn,
                    Version = dto.Version,
                    IsDeleted = dto.IsDeleted,
                    CreationTimeUtc = dto.CreationTimeUtc,
                    LastWriteTimeUtc = dto.LastWriteTimeUtc
                };
            }

            private PointerFileEntryDto CreatePointerFileEntryDto(PointerFileEntry2 pfe)
            {
                var rn = ToPlatformNeutralPath(pfe.RelativeName);
                rn = StringCipher.Encrypt(rn, _passphrase);

                return new PointerFileEntryDto()
                {
                    PartitionKey = pfe.ManifestHash.Value,
                    RowKey = $"{GetRelativeNameHash(pfe.RelativeName)}-{pfe.Version.Ticks * -1:x8}", //Make ticks negative so when lexicographically sorting the RowKey the most recent version is on top

                    EncryptedRelativeName = rn,
                    Version = pfe.Version,
                    IsDeleted = pfe.IsDeleted,
                    CreationTimeUtc = pfe.CreationTimeUtc,
                    LastWriteTimeUtc = pfe.LastWriteTimeUtc,
                };
            }




            private string ToPlatformNeutralPath(string platformSpecificPath) => platformSpecificPath.Replace(Path.DirectorySeparatorChar, '/');
            private string ToPlatformSpecificPath(string platformNeutralPath) => platformNeutralPath.Replace('/', Path.DirectorySeparatorChar);




            private class PointerFileEntryDto : TableEntity
            {
                public string EncryptedRelativeName { get; init; }
                public DateTime Version { get; init; }
                public bool IsDeleted { get; init; }
                public DateTime? CreationTimeUtc { get; init; }
                public DateTime? LastWriteTimeUtc { get; init; }


                [IgnoreProperty] 
                internal string RelativeNameHash => RowKey.Substring(0, 8);

                public override int GetHashCode()
                {
                    return HashCode.Combine(
                        RelativeNameHash,
                        //obj.Version,  //DO NOT Compare on DateTime Version
                        IsDeleted,
                        CreationTimeUtc,
                        LastWriteTimeUtc);
                }
            }
        }

        public record PointerFileEntry2
        {
            public HashValue ManifestHash { get; init; }
            public string RelativeName { get; init; }
            private string RelativeNameHash => PointerFileEntryRepository.GetRelativeNameHash(RelativeName);
            public DateTime Version { get; init; }
            public bool IsDeleted { get; init; }
            public DateTime? CreationTimeUtc { get; init; }
            public DateTime? LastWriteTimeUtc { get; init; }

            public override int GetHashCode()
            {
                return HashCode.Combine(
                    RelativeNameHash,
                    //obj.Version,  //DO NOT Compare on DateTime Version
                    IsDeleted,
                    CreationTimeUtc,
                    LastWriteTimeUtc);
            }
        }


        


        //public class PointerFileEntry3 : TableEntity
        //{
            


        //    public PointerFileEntry3()
        //    {
        //    }
        //    public PointerFileEntry3(PointerFile pf, DateTime version) : this(pf.Hash, pf.RelativeName, version)
        //    {
        //    }
        //    public PointerFileEntry3(HashValue hash, string relativeName, DateTime version)
        //    {
        //        //var xxx = $"{BitConverter.ToString(SHA256.HashData(pf.RelativeName.Select(c => Convert.ToByte(c)).ToArray())):x8}".ToLower().Replace("-","");

        //        PartitionKey = hash.Value;
        //        RowKey = $"{GetRelativeNameHash(relativeName)}-{version.Ticks *-1:x8}"; //Make ticks negative so when lexicographically sorting the RowKey the most recent version is on top
        //    }

        //    public string RelativeName { get; set; }
        //    public DateTime Version { get; set; }
        //    public bool IsDeleted { get; set; }
        //    public DateTime? CreationTimeUtc { get; set; }
        //    public DateTime? LastWriteTimeUtc { get; set; }

        //    [IgnoreProperty]
        //    public HashValue Hash => new() {Value = PartitionKey};
        //}
    }
}
