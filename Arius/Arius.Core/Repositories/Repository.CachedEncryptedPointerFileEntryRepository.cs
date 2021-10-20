using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Arius.Core.Extensions;
using Arius.Core.Models;
using Arius.Core.Services;
using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using Murmur;

namespace Arius.Core.Repositories
{
    internal partial class Repository
    {
        private class CachedEncryptedPointerFileEntryRepository
        {
            public CachedEncryptedPointerFileEntryRepository(ILogger logger, IOptions options)
            {
                this.logger = logger;
                this.passphrase = options.Passphrase;

                entries = new(logger, 
                    options.AccountName, options.AccountKey, $"{options.Container}{TableNameSuffix}",
                    ConvertToDto, ConvertFromDto);

                versionsTask = Task.Run(async () =>
                {
                    var pfes = await entries.GetAllAsync();

                    var r = pfes
                        .Select(a => a.VersionUtc)
                        .Distinct()
                        .OrderBy(x => x)
                        .ToList();

                    return r;
                });
            }

            internal const string TableNameSuffix = "pointerfileentries";

            private readonly ILogger logger;
            private readonly string passphrase;

            private readonly EagerCachedConcurrentDataTableRepository<PointerFileEntryDto, PointerFileEntry> entries;
            private readonly Task<List<DateTime>> versionsTask;

            private readonly static PointerFileEntryEqualityComparer equalityComparer = new();


            public async Task<IReadOnlyCollection<PointerFileEntry>> GetEntriesAsync()
            {
                return await entries.GetAllAsync();
            }

            /// <summary>
            /// Get the versions in universal time
            /// </summary>
            /// <returns></returns>
            public async Task<IReadOnlyList<DateTime>> GetVersionsAsync()
            {
                return await versionsTask;
            }


            private readonly SemaphoreSlim semaphoreSlim = new(1, 1);

            /// <summary>
            /// Insert the PointerFileEntry into the table storage, if a similar entry (according to the PointerFileEntryEqualityComparer) does not yet exist
            /// </summary>
            /// <param name="pfe"></param>
            /// <returns>Returns true if an entry was actually added / the collection was modified</returns>
            public async Task<bool> CreatePointerFileEntryIfNotExistsAsync(PointerFileEntry pfe)
            {
                //Asynchronously wait to enter the Semaphore
                await semaphoreSlim.WaitAsync();
                try
                {
                    var pfes = await entries.GetAllAsync();

                    var lastVersion = pfes.AsParallel()
                        .Where(p => pfe.RelativeName.Equals(p.RelativeName))
                        .OrderBy(p => p.VersionUtc)
                        .LastOrDefault();

                    var toAdd = !equalityComparer.Equals(pfe, lastVersion); //if the last version of the PointerFileEntry is not equal -- insert a new one

                    if (toAdd)
                    {
                        //Insert the new PointerFileEntry
                        await entries.Add(pfe);

                        //Insert the version
                        var versions = await versionsTask;
                        if (!versions.Contains(pfe.VersionUtc))
                            versions.Add(pfe.VersionUtc); //TODO: aan het einde?
                    }

                    return toAdd;
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Error", pfe);
                    throw;
                }
                finally
                {
                    semaphoreSlim.Release();
                }
            }


            private PointerFileEntry ConvertFromDto(PointerFileEntryDto dto)
            {
                var rn = CryptoService.Decrypt(dto.EncryptedRelativeName, passphrase);
                rn = ToPlatformSpecificPath(rn);

                return new()
                {
                    BinaryHash = new BinaryHash(dto.PartitionKey),
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
                rn = CryptoService.Encrypt(rn, passphrase);

                return new()
                {
                    PartitionKey = pfe.BinaryHash.Value,
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



            private class PointerFileEntryDto : ITableEntity
            {
                public string PartitionKey { get; set; }
                public string RowKey { get; set; }
                public DateTimeOffset? Timestamp { get; set; }
                public ETag ETag { get; set; }

                public string EncryptedRelativeName { get; init; }
                public DateTime Version { get; init; }
                public bool IsDeleted { get; init; }
                public DateTime? CreationTimeUtc { get; init; }
                public DateTime? LastWriteTimeUtc { get; init; }
            }
        }
    }
}