using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Arius.Core.Commands;
using Arius.Core.Extensions;
using Arius.Core.Models;
using Microsoft.Azure.Cosmos.Table;
using Microsoft.Extensions.Logging;
using Murmur;

namespace Arius.Core.Repositories
{
    internal partial class Repository
    {
        internal const string TableNameSuffix = "pointers";

        private class CachedEncryptedPointerFileEntryRepository
        {
            public CachedEncryptedPointerFileEntryRepository(IOptions options, ILogger logger)
            {
                this.logger = logger;

                passphrase = options.Passphrase;

                var connectionString = $"DefaultEndpointsProtocol=https;AccountName={options.AccountName};AccountKey={options.AccountKey};EndpointSuffix=core.windows.net";

                var csa = CloudStorageAccount.Parse(connectionString);
                var tc = csa.CreateCloudTableClient();
                table = tc.GetTableReference($"{options.Container}{TableNameSuffix}");

                var r = table.CreateIfNotExists();
                if (r)
                    logger.LogInformation($"Created tables for {options.Container}... ");

                //Asynchronously download all PointerFileEntryDtos
                pointerFileEntries = Task.Run(() =>
                {
                    // get all rows - we're getting them anyway as GroupBy is not natively supported
                    var r = table
                        .CreateQuery<PointerFileEntryDto>()
                        .AsEnumerable()
                        .Select(dto => ConvertFromDto(dto))
                        .ToList();

                    return r;
                });

                versions = new(async () =>
                {
                    var pfes = await pointerFileEntries;

                    var r = pfes
                        .Select(a => a.VersionUtc)
                        .Distinct()
                        .OrderBy(a => a)
                        .ToList();

                    return r;
                });
            }

            private readonly ILogger logger;
            private readonly CloudTable table;
            private readonly Task<List<PointerFileEntry>> pointerFileEntries;
            private readonly AsyncLazy<List<DateTime>> versions;
            private readonly string passphrase;
            private readonly static PointerFileEntryEqualityComparer equalityComparer = new();


            public async Task<IReadOnlyList<PointerFileEntry>> GetEntriesAsync()
            {
                return await pointerFileEntries;
            }

            /// <summary>
            /// Get the versions in universal time
            /// </summary>
            /// <returns></returns>
            public async Task<IReadOnlyList<DateTime>> GetVersionsAsync()
            {
                return await versions;
            }


            /// <summary>
            /// Insert the PointerFileEntry into the table storage, if a similar entry (according to the PointerFileEntryEqualityComparer) does not yet exist
            /// </summary>
            /// <param name="pfe"></param>
            /// <returns>Returns true if an entry was actually added / the collection was modified</returns>
            public async Task<bool> CreatePointerFileEntryIfNotExistsAsync(PointerFileEntry pfe)
            {
                try
                {
                    //Upsert into Cache
                    var pfes = await pointerFileEntries;

                    bool toAdd = false;

                    lock (pfes)
                    {
                        var lastVersion = pfes.AsParallel()
                            .Where(p => pfe.RelativeName.Equals(p.RelativeName))
                            .OrderBy(p => p.VersionUtc)
                            .LastOrDefault();

                        if (!equalityComparer.Equals(pfe, lastVersion))
                        //if (!pfes.Contains(pfe, equalityComparer))
                        {
                            //Remove the old value, if present
                            //var pfeToRemove = pfes.SingleOrDefault(pfe2 => pfe.ManifestHash.Equals(pfe2.ManifestHash) && pfe.RelativeName.Equals(pfe2.RelativeName));
                            //pfes.Remove(pfeToRemove);

                            //Add the new value to the cache
                            pfes.Add(pfe);
                            toAdd = true;
                        }
                    }

                    if (toAdd)
                    {
                        //Insert into Table Storage
                        var dto = ConvertToDto(pfe);
                        var op = TableOperation.Insert(dto);
                        await table.ExecuteAsync(op);

                        //Insert into the versions
                        var vs = await versions;
                        lock (vs)
                        {
                            if (!vs.Contains(pfe.VersionUtc))
                                vs.Add(pfe.VersionUtc); //TODO: aan het einde?
                        }
                    }

                    return toAdd;
                }
                catch (StorageException e)
                {
                    logger.LogError(e, "Error", pfe); //TODO
                    throw;
                }
            }


            private PointerFileEntry ConvertFromDto(PointerFileEntryDto dto)
            {
                var rn = StringCipher.Decrypt(dto.EncryptedRelativeName, passphrase);
                rn = ToPlatformSpecificPath(rn);

                return new()
                {
                    ManifestHash = new HashValue() { Value = dto.PartitionKey },
                    RelativeName = rn,
                    VersionUtc = dto.Version,
                    IsDeleted = dto.IsDeleted,
                    CreationTimeUtc = dto.CreationTimeUtc,
                    LastWriteTimeUtc = dto.LastWriteTimeUtc
                };
            }

            private PointerFileEntryDto ConvertToDto(PointerFileEntry pfe)
            {
                var rn = ToPlatformNeutralPath(pfe.RelativeName);
                rn = StringCipher.Encrypt(rn, passphrase);

                return new PointerFileEntryDto()
                {
                    PartitionKey = pfe.ManifestHash.Value,
                    RowKey = $"{GetRelativeNameHash(pfe.RelativeName)}-{pfe.VersionUtc.Ticks * -1:x8}", //Make ticks negative so when lexicographically sorting the RowKey the most recent version is on top

                    EncryptedRelativeName = rn,
                    Version = pfe.VersionUtc,
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

                var bytes = murmurHash.ComputeHash(Encoding.UTF8.GetBytes(neutralRelativeName));
                var hex = Convert.ToHexString(bytes).ToLower();

                return hex;
            }
            private static readonly HashAlgorithm murmurHash = MurmurHash.Create32();


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
            }
        }
    }
}