﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Arius.Core.Models;
using Microsoft.Extensions.Logging;

namespace Arius.Core.Repositories
{
    internal partial class Repository
    {
        public enum CreatePointerFileEntryResult
        {
            Upserted,
            InsertedDeleted,
            NoChange
        }

        
        /// <summary>
        /// Create a PointerFileEntry for the given PointerFile and the given version
        /// </summary>
        public async Task<CreatePointerFileEntryResult> CreatePointerFileEntryIfNotExistsAsync(PointerFile pf, DateTime versionUtc)
        {
            var pfe = new PointerFileEntry()
            {
                BinaryHash = pf.Hash,
                RelativeName = pf.RelativeName,
                VersionUtc = versionUtc,
                IsDeleted = false,
                CreationTimeUtc = File.GetCreationTimeUtc(pf.FullName).ToUniversalTime(),
                LastWriteTimeUtc = File.GetLastWriteTimeUtc(pf.FullName).ToUniversalTime(),
            };

            return await CreatePointerFileEntryIfNotExistsAsync(pfe);
        }

        /// <summary>
        /// Create a PointerFileEntry that is deleted form the given PointerFileEntry
        /// </summary>
        public async Task<CreatePointerFileEntryResult> CreateDeletedPointerFileEntryAsync(PointerFileEntry pfe, DateTime versionUtc)
        {
            pfe = pfe with
            {
                VersionUtc = versionUtc,
                IsDeleted = true,
                CreationTimeUtc = null,
                LastWriteTimeUtc = null
            };

            return await CreatePointerFileEntryIfNotExistsAsync(pfe);
        }

        private async Task<CreatePointerFileEntryResult> CreatePointerFileEntryIfNotExistsAsync(PointerFileEntry pfe)
        {
            if (await pfeRepo.CreatePointerFileEntryIfNotExistsAsync(pfe))
            {
                //We inserted the entry
                if (pfe.IsDeleted)
                {
                    logger.LogInformation($"Deleted '{pfe.RelativeName}'");
                    return CreatePointerFileEntryResult.InsertedDeleted;
                }
                else
                {
                    logger.LogInformation($"Added '{pfe.RelativeName}'");
                    return CreatePointerFileEntryResult.Upserted;
                }
            }

            return CreatePointerFileEntryResult.NoChange;
        }

        internal async Task<IEnumerable<PointerFileEntry>> GetCurrentEntries(bool includeDeleted)
        {
            return await GetPointerFileEntries(DateTime.Now, includeDeleted);
        }

        /// <summary>
        /// Get the PointerFileEntries at the given version.
        /// If no version is specified, the current (most recent) will be returned
        /// </summary>
        public async Task<IEnumerable<PointerFileEntry>> GetPointerFileEntries(DateTime pointInTime, bool includeDeleted)
        {
            var pfes = await GetPointerFileEntriesAtPointInTimeAsync(pointInTime);

            if (includeDeleted)
                return pfes;
            else
                return pfes.Where(pfe => !pfe.IsDeleted);
        }

        private async Task<IReadOnlyList<PointerFileEntry>> GetPointerFileEntriesAtPointInTimeAsync(DateTime pointInTime)
        {
            try
            {
                var version = await GetVersionAsync(pointInTime);

                var r = await GetPointerFileEntriesAtVersionAsync(version);

                return r;
            }
            catch (ArgumentException e)
            {
                logger.LogWarning($"{e.Message} Returning empty list of PointerFileEntries.");

                return Array.Empty<PointerFileEntry>();
            }
        }

        private async Task<IReadOnlyList<PointerFileEntry>> GetPointerFileEntriesAtVersionAsync(DateTime versionUtc)
        {
            var pfes = await GetPointerFileEntriesAsync();

            //TODO an exception here is swallowed

            var r = pfes
                .GroupBy(pfe => pfe.RelativeName)
                .Select(g => g.Where(pfe => pfe.VersionUtc <= versionUtc)).Where(c => c.Any())
                .Select(z => z.OrderBy(pfe => pfe.VersionUtc).Last()).ToList();

            return r;
        }

        /// <summary>
        /// Get All PointerFileEntries
        /// </summary>
        /// <returns></returns>
        internal async Task<IReadOnlyCollection<PointerFileEntry>> GetPointerFileEntriesAsync()
        {
            return await pfeRepo.GetEntriesAsync();
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
                throw new ArgumentException($"{nameof(GetVersionAsync)}: No version found for {nameof(pointInTime)} {pointInTime}.");

            return version.Value;
        }

        /// <summary>
        /// Returns an chronologically ordered list of versions (in universal time) for this repository
        /// </summary>
        /// <returns></returns>
        public async Task<IEnumerable<DateTime>> GetVersionsAsync()
        {
            return await pfeRepo.GetVersionsAsync();
        }
    }
}
