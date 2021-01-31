﻿using System;
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
        public static readonly string TableNameSuffix = "pointers";

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
                    _pointerFileEntries = Task.Run(() => GetStateOn(DateTime.Now.ToUniversalTime()));
                }

                private readonly ILogger<CachedEncryptedPointerFileEntryRepository> _logger;
                private readonly CloudTable _pointerEntryTable;
                private readonly Task<List<PointerFileEntry>> _pointerFileEntries;
                private readonly string _passphrase;


                public Task<List<PointerFileEntry>> CurrentEntries => _pointerFileEntries;

                public async Task InsertPointerFileEntry(PointerFileEntry pfe)
                {
                    try
                    {
                        //Upsert into Cache
                        var pfes = await _pointerFileEntries;
                            //Remove the old value, if present
                        var pfeToRemove = pfes.SingleOrDefault(pfe2 => pfe.ManifestHash.Equals(pfe2.ManifestHash) && pfe.RelativeName == pfe2.RelativeName);
                        pfes.Remove(pfeToRemove);
                            //Add the new value
                        pfes.Add(pfe);

                        //Insert into Table Storage
                        var dto = CreatePointerFileEntryDto(pfe);
                        var op = TableOperation.Insert(dto);
                        await _pointerEntryTable.ExecuteAsync(op);
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
                        .Replace(Path.DirectorySeparatorChar, '/');

                    var bytes = _murmurHash.ComputeHash(Encoding.UTF8.GetBytes(neutralRelativeName));
                    var hex = Convert.ToHexString(bytes).ToLower();

                    return hex;
                }
                private static readonly HashAlgorithm _murmurHash = MurmurHash.Create32();


                private List<PointerFileEntry> GetStateOn(DateTime pointInTime)
                {
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



                    var r = _pointerEntryTable
                        .CreateQuery<PointerFileEntryDto>()
                        .AsEnumerable()
                        .GroupBy(pfe => pfe.RelativeNameHash)   //more or less equiv as GroupBy(RelativeName) but the hash is tolower and platform neutral
                        .Select(g => g
                            .Where(dto => dto.Version <= pointInTime)
                            .OrderBy(dto => dto.Version).Last())
                        .Select(dto => CreatePointerFileEntry(dto));

                    //var xxx = zz.Except(r).ToList();

                    return r.ToList();

                    


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
                }
            }
        }
    }
}