using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security;
using System.Threading.Tasks;
using Arius.Core.Commands;
using Arius.Extensions;
using Arius.Models;
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

                var r = await GetEntriesAtVersionAsync(version);

                return r;
            }

            /// <summary>
            /// Get the version that corresponds to the state of the archive at pointInTime
            /// </summary>
            /// <param name="pointInTime"></param>
            /// <returns></returns>
            private async Task<DateTime> GetVersionAsync(DateTime pointInTime)
            {
                var versions = (await GetVersionsAsync()).Reverse();

                // if the pointInTime is a version - return the pointInTime
                if (versions.Contains(pointInTime))
                    return pointInTime;

                // else, search for the version that exactly precedes the pointInTime
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
            }

            /// <summary>
            /// Returns an chronologically ordered list of versions (in universal time) for this repository
            /// </summary>
            /// <returns></returns>
            public async Task<IEnumerable<DateTime>> GetVersionsAsync()
            {
                return await _repo.GetVersionsAsync();
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
