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
using Murmur;

namespace Arius.Repositories
{


    internal partial class AzureRepository
    {
        internal const string TableNameSuffix = "pointers";

        // TODO KARL quid pattern of nested pratial classes
        private partial class PointerFileEntryRepository
        {
            private class CachedEncryptedPointerFileEntryRepository
            {
                public CachedEncryptedPointerFileEntryRepository(ICommandExecutorOptions options, ILogger<CachedEncryptedPointerFileEntryRepository> logger)
                {
                    _logger = logger;

                    var o = (IAzureRepositoryOptions) options;

                    _passphrase = o.Passphrase;

                    var connectionString = $"DefaultEndpointsProtocol=https;AccountName={o.AccountName};AccountKey={o.AccountKey};EndpointSuffix=core.windows.net";

                    var csa = CloudStorageAccount.Parse(connectionString);
                    var tc = csa.CreateCloudTableClient();
                    _pointerEntryTable = tc.GetTableReference($"{o.Container}{TableNameSuffix}");

                    var r = _pointerEntryTable.CreateIfNotExists();
                    if (r)
                        _logger.LogInformation($"Created tables for {o.Container}... ");

                    //Asynchronously download all PointerFileEntryDtos
                    _pointerFileEntries = new(() =>
                    {
                        // get all rows - we're getting them anyway as GroupBy is not natively supported
                        var r = _pointerEntryTable
                            .CreateQuery<PointerFileEntryDto>()
                            .AsEnumerable()
                            .Select(dto => CreatePointerFileEntry(dto))
                            .ToList();

                        return r;
                    });
                }

                private readonly ILogger<CachedEncryptedPointerFileEntryRepository> _logger;
                private readonly CloudTable _pointerEntryTable;
                private readonly AsyncLazy<List<PointerFileEntry>> _pointerFileEntries;
                private readonly string _passphrase;


                /// <summary>
                /// Get the entries at the specified version. 
                /// If no version is specified, the current/latest version is used.
                /// </summary>
                /// <param name="version"></param>
                /// <returns></returns>
                public async Task<IReadOnlyList<PointerFileEntry>> GetEntries(DateTime pointInTime)
                {
                    var versions = (await GetVersions()).Reverse();
                    DateTime? version = null;
                    foreach (var v in versions)
                    {
                        if (pointInTime >= v)
                        {
                            version = v;
                            break;
                        }
                    }

                    if (version is null)
                        throw new ArgumentException($"No backup version found for pointInTime {pointInTime}");

                    var pfes = await _pointerFileEntries;

                    //TODO an exception here is swallowed

                    lock (entriesPerVersionLock)
                    {
                        if (!entriesPerVersion.ContainsKey(version.Value))
                        {
                            var entriesForThisVersion = pfes
                                .GroupBy(pfe => pfe.RelativeName)
                                .Select(g => g.Where(pfe => pfe.Version <= version.Value)).Where(c => c.Any())
                                .Select(z => z.OrderBy(pfe => pfe.Version).Last()).ToList();

                            entriesPerVersion.Add(
                                version.Value,
                                entriesForThisVersion);
                        }
                    }

                    return entriesPerVersion[version.Value];
                }
                private readonly Dictionary<DateTime, List<PointerFileEntry>> entriesPerVersion = new();
                private readonly object entriesPerVersionLock = new object();

                /// <summary>
                /// Returns an chronologically ordered list of versions (in Local times) for this repository
                /// </summary>
                /// <returns></returns>
                public async Task<IEnumerable<DateTime>> GetVersions()
                {
                    var pfes = await _pointerFileEntries;

                    lock (versionsLock)
                    {
                        if (versions is null) //TODO cannot lock on (versions = null) so find another mechanism?
                        {
                            versions = pfes
                                .Select(a => a.Version.ToLocalTime())
                                .Distinct()
                                .OrderBy(a => a)
                                .ToList();
                        }
                    }

                    return versions;
                }
                private IEnumerable<DateTime> versions = null;
                private readonly object versionsLock = new object();


                private static readonly PointerFileEntryEqualityComparer _pfeec = new();

                /// <summary>
                /// 
                /// </summary>
                /// <param name="pfe"></param>
                /// <returns>Returns true if an entry was actually added / the collection was modified</returns>
                public async Task<bool> CreatePointerFileEntryIfNotExistsAsync(PointerFileEntry pfe)
                {
                    try
                    {
                        //Upsert into Cache
                        var pfes = await _pointerFileEntries;

                        bool toAdd = false;

                        lock (pfes)
                        {
                            if (!pfes.Contains(pfe, _pfeec))
                            {
                                //Remove the old value, if present
                                var pfeToRemove = pfes.SingleOrDefault(pfe2 => pfe.ManifestHash.Equals(pfe2.ManifestHash) && pfe.RelativeName.Equals(pfe2.RelativeName));
                                pfes.Remove(pfeToRemove);

                                //Add the new value
                                pfes.Add(pfe);
                                toAdd = true;
                            }
                        }

                        if (toAdd)
                        {
                            //Insert into Table Storage
                            var dto = CreatePointerFileEntryDto(pfe);
                            var op = TableOperation.Insert(dto);
                            await _pointerEntryTable.ExecuteAsync(op);
                        }

                        return toAdd;
                    }
                    catch (StorageException)
                    {
                        //Console.WriteLine(e.Message);
                        //Console.ReadLine();
                        throw;
                    }
                }

                private PointerFileEntryDto CreatePointerFileEntryDto(PointerFileEntry pfe)
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

                private static string GetRelativeNameHash(string relativeName)
                {
                    var neutralRelativeName = relativeName
                        .ToLower(CultureInfo.InvariantCulture)
                        .Replace(Path.DirectorySeparatorChar, PlatformNeutralDirectorySeparatorChar);

                    var bytes = _murmurHash.ComputeHash(Encoding.UTF8.GetBytes(neutralRelativeName));
                    var hex = Convert.ToHexString(bytes).ToLower();

                    return hex;
                }
                private static readonly HashAlgorithm _murmurHash = MurmurHash.Create32();


                

                private List<PointerFileEntry> GetStateOn(DateTime pointInTime)
                {
                    return null;


                    /* //TODO KARL is this optimal?
                     * https://docs.microsoft.com/en-us/azure/cosmos-db/table-storage-design-guide#solution-6
                     *  Table storage is lexicographically ordered ?
                     */

                    //TODO karl multithreading debugging

                    //var zz = _pointerEntryTable
                    //    .CreateQuery<PointerFileEntryDto>().AsEnumerable()
                    //    .Select(dto => CreatePointerFileEntry(dto)).ToArray();

                    //var zzzz = zz.Where(pfe => pfe.IsDeleted).ToArray();

                    //var zzz = _pointerEntryTable
                    //    .CreateQuery<PointerFileEntryDto>().AsEnumerable()
                    //    .Select(dto => CreatePointerFileEntry(dto))
                    //    .GroupBy(pfe => pfe.RelativeName)
                    //    .Where(z => z.Count() > 1)
                    //    .ToArray();

                    //var zzzz = _pointerEntryTable
                    //    .CreateQuery<PointerFileEntryDto>().AsEnumerable()
                    //    .Select(dto => CreatePointerFileEntry(dto))
                    //    .GroupBy(pfe => pfe.ManifestHash)
                    //    .Where(z => z.Count() > 1)
                    //    .ToArray();



                    //var r = _pointerEntryTable
                    //    .CreateQuery<PointerFileEntryDto>()
                    //    .AsEnumerable()
                    //    .GroupBy(pfe => pfe.RelativeNameHash)   //more or less equiv as GroupBy(RelativeName) but the hash is tolower and platform neutral
                    //    .Select(g => g
                    //        .Where(dto => dto.Version <= pointInTime)
                    //        .OrderBy(dto => dto.Version).Last())
                    //    .Select(dto => CreatePointerFileEntry(dto));







                    //var a0 = _pointerEntryTable
                    //    .CreateQuery<PointerFileEntryDto>()
                    //    .AsEnumerable().ToList();

                    //var a = a0
                    //    .GroupBy(pfe => pfe.RelativeNameHash).ToList();

                    //var b = a.Select(g => g
                    //        .Where(dto => dto.Version <= pointInTime)
                    //        .OrderBy(dto => dto.Version).Last());

                    //var c = b.Select(dto => CreatePointerFileEntry(dto));






                    ////////// get all rows - we're getting them anyway as GroupBy is not natively supported
                    ////////var r0 = _pointerEntryTable
                    ////////    .CreateQuery<PointerFileEntryDto>()
                    ////////    .AsEnumerable().ToArray();

                    ////////var r = r0
                    ////////    .Select(dto => CreatePointerFileEntry(dto))
                    ////////    .GroupBy(pfe => pfe.RelativeName)
                    ////////    .Select(g => g
                    ////////        .Where(pfe => pfe.Version <= pointInTime)
                    ////////        .OrderBy(pfe => pfe.Version).Last())
                    ////////    .ToList();

                    //var xxx = zz.Except(r).ToList();

                    


                    //var r = _pointerEntryTable
                    //    .CreateQuery<PointerFileEntryDto>()
                    //    .AsEnumerable()
                    //    .GroupBy(pfe => pfe.RelativeNameHash)   //more or less equiv as GroupBy(RelativeName) but the hash is tolower and platform neutral
                    //    .Select(g => g
                    //        .Where(dto => dto.Version <= pointInTime)
                    //        .OrderBy(dto => dto.Version).Last())
                    //    .Select(dto => CreatePointerFileEntry(dto))
                    //    .ToDictionary(pfe => pfe.ManifestHash, pfe => new Dictionary<string, PointerFileEntry>()
                    //    {
                    //        {  pfe.RelativeName, pfe }
                    //    });

                    //return r;
                }



                private PointerFileEntry CreatePointerFileEntry(PointerFileEntryDto dto)
                {
                    var rn = StringCipher.Decrypt(dto.EncryptedRelativeName, _passphrase);
                    rn = ToPlatformSpecificPath(rn);

                    return new()
                    {
                        ManifestHash = new HashValue() {Value = dto.PartitionKey},
                        RelativeName = rn,
                        Version = dto.Version,
                        IsDeleted = dto.IsDeleted,
                        CreationTimeUtc = dto.CreationTimeUtc,
                        LastWriteTimeUtc = dto.LastWriteTimeUtc
                    };
                }

                private const char PlatformNeutralDirectorySeparatorChar = '/';
                private static string ToPlatformNeutralPath(string platformSpecificPath) => platformSpecificPath.Replace(Path.DirectorySeparatorChar, PlatformNeutralDirectorySeparatorChar);
                private static string ToPlatformSpecificPath(string platformNeutralPath) => platformNeutralPath.Replace(PlatformNeutralDirectorySeparatorChar, Path.DirectorySeparatorChar);
                
                private class PointerFileEntryDto : TableEntity
                {
                    public string EncryptedRelativeName { get; init; }
                    public DateTime Version { get; init; }
                    public bool IsDeleted { get; init; }
                    public DateTime? CreationTimeUtc { get; init; }
                    public DateTime? LastWriteTimeUtc { get; init; }


                    [IgnoreProperty]
                    internal string RelativeNameHash => RowKey.Substring(0, 8);
                }
            }
        }
    }
}