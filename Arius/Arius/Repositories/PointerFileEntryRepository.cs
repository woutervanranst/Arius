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
using Microsoft.Azure.Cosmos.Table;
using Microsoft.Extensions.Logging;

namespace Arius.Repositories
{
    internal partial class AzureRepository
    {
        private partial class PointerFileEntryRepository
        {
            public PointerFileEntryRepository(ICommandExecutorOptions options, ILogger<PointerFileEntryRepository> logger, ILoggerFactory loggerFactory)
            {
                _logger = logger;
                _repo = new CachedEncryptedPointerFileEntryRepository(options, loggerFactory.CreateLogger<CachedEncryptedPointerFileEntryRepository>());
            }

            private readonly ILogger<PointerFileEntryRepository> _logger;
            private readonly CachedEncryptedPointerFileEntryRepository _repo;


            /// <summary>
            /// Create a PointerFileEntry for the given PointerFile and the given version
            /// </summary>
            public async Task CreatePointerFileEntryIfNotExistsAsync(PointerFile pf, DateTime version)
            {
                var pfe = new PointerFileEntry()
                {
                    ManifestHash = pf.Hash,
                    RelativeName = pf.RelativeName,
                    Version = version,
                    IsDeleted = false,
                    CreationTimeUtc = File.GetCreationTimeUtc(pf.FullName).ToUniversalTime(), //TODO
                    LastWriteTimeUtc = File.GetLastWriteTimeUtc(pf.FullName).ToUniversalTime(),
                };

                await CreatePointerFileEntryIfNotExistsAsync(pfe);

            }

            /// <summary>
            /// Create a PointerFileEntry that is deleted form the given PointerFileEntry
            /// </summary>
            public async Task CreateDeletedPointerFileEntryAsync(PointerFileEntry pfe, DateTime version)
            {
                pfe = pfe with
                {
                    Version = version,
                    IsDeleted = true,
                    CreationTimeUtc = null,
                    LastWriteTimeUtc = null
                };

                await CreatePointerFileEntryIfNotExistsAsync(pfe);
            }

            private async Task CreatePointerFileEntryIfNotExistsAsync(PointerFileEntry pfe)
            {
                if (await _repo.CreatePointerFileEntryIfNotExistsAsync(pfe))
                {
                    //We inserted the entry
                    //UpdateEntriesAtVersion(pfe);

                    if (pfe.IsDeleted)
                        _logger.LogInformation($"Deleted {pfe.RelativeName}");
                    else
                        _logger.LogInformation($"Added {pfe.RelativeName}");
                }
            }


            /// <summary>
            /// Get the PointerFileEntries at the given version.
            /// If no version is specified, the current (most recent) will be returned
            /// </summary>
            public async Task<IEnumerable<PointerFileEntry>> GetEntries(DateTime pointInTime, bool includeDeleted)
            {
                var pfes = await GetEntriesAtPointInTimeAsync(pointInTime);

                if (includeDeleted)
                    return pfes;
                else
                    return pfes.Where(pfe => !pfe.IsDeleted);
            }

            private async Task<IReadOnlyList<PointerFileEntry>> GetEntriesAtPointInTimeAsync(DateTime pointInTime)
            {
                var version = await GetVersionAsync(pointInTime);

                return await GetEntriesAtVersionAsync(version);
            }

            /// <summary>
            /// Get the version that corresponds to the state of the archive at pointInTime
            /// </summary>
            /// <param name="pointInTime"></param>
            /// <returns></returns>
            private async Task<DateTime> GetVersionAsync(DateTime pointInTime)
            {
                var versions = (await GetVersionsAsync()).Reverse();
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

                return version.Value;
            }

            private async Task<IReadOnlyList<PointerFileEntry>> GetEntriesAtVersionAsync(DateTime version)
            {
                var pfes = await _repo.GetEntriesAsync();

                //TODO an exception here is swallowed

                var r = pfes
                    .GroupBy(pfe => pfe.RelativeName)
                    .Select(g => g.Where(pfe => pfe.Version <= version)).Where(c => c.Any())
                    .Select(z => z.OrderBy(pfe => pfe.Version).Last()).ToList();

                return r;

                //lock (entriesPerVersionLock)
                //{
                //    if (!entriesPerVersion.ContainsKey(version))
                //    {
                //        var entriesForThisVersion = pfes
                //            .GroupBy(pfe => pfe.RelativeName)
                //            .Select(g => g.Where(pfe => pfe.Version <= version)).Where(c => c.Any())
                //            .Select(z => z.OrderBy(pfe => pfe.Version).Last()).ToList();

                //        entriesPerVersion.Add(
                //            version,
                //            entriesForThisVersion);
                //    }
                //}

                //return entriesPerVersion[version];
            }
            //private readonly Dictionary<DateTime, List<PointerFileEntry>> entriesPerVersion = new();
            //private readonly object entriesPerVersionLock = new object();

            //private void UpdateEntriesAtVersion(PointerFileEntry pfe)
            //{
            //    lock (entriesPerVersionLock)
            //    {
            //        if (entriesPerVersion.ContainsKey(pfe.Version))
            //            entriesPerVersion[pfe.Version].Add(pfe);
            //        else
            //            entriesPerVersion.Add(pfe.Version, new() { pfe });
            //    }
            //}

            


            /// <summary>
            /// Returns an chronologically ordered list of versions (in universal time) for this repository
            /// </summary>
            /// <returns></returns>
            public async Task<IEnumerable<DateTime>> GetVersionsAsync()
            {
                return await _repo.GetVersionsAsync();
            }



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
        }

        public record PointerFileEntry
        {
            internal HashValue ManifestHash { get; init; }
            public string RelativeName { get; init; }

            /// <summary>
            /// Version (in Universal Time)
            /// </summary>
            public DateTime Version { get; init; }
            public bool IsDeleted { get; init; }
            public DateTime? CreationTimeUtc { get; init; }
            public DateTime? LastWriteTimeUtc { get; init; }
        }
    }
}
