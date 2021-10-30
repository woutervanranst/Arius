using Arius.Core.Extensions;
using Arius.Core.Models;
using Azure.Storage.Blobs;
using ConcurrentCollections;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Arius.Core.Repositories;

internal partial class Repository
{
    internal class PointerFileEntryRepository : AppendOnlyRepository<PointerFileEntry>
    {
        public PointerFileEntryRepository(ILogger<PointerFileEntryRepository> logger, IOptions options, BlobContainerClient container)
            : base(logger, options, container, PointerFileEntriesFolderName)
        {

        }

        private const string PointerFileEntriesFolderName = "pointerfileentries";

        private readonly TaskCompletionSource<SortedSet<DateTime>> versionsTask = new();
        private readonly static PointerFileEntryEqualityComparer equalityComparer = new();

        
        protected override async Task<ConcurrentHashSet<PointerFileEntry>> LoadEntriesAsync(BlobContainerClient container)
        {
            var r = await base.LoadEntriesAsync(container);

            versionsTask.SetResult(new SortedSet<DateTime>(r.Select(pfe => pfe.VersionUtc).Distinct()));

            return r;
        }

        /// <summary>
        /// Insert the PointerFileEntry into the table storage, if a similar entry (according to the PointerFileEntryEqualityComparer) does not yet exist
        /// </summary>
        /// <param name="pfe"></param>
        public async Task<bool> CreatePointerFileEntryIfNotExistsAsync(PointerFileEntry pfe)
        {
            var entries = await GetEntriesAsync();

            var lastVersion = entries.AsParallel()
                .Where(p => pfe.RelativeName.Equals(p.RelativeName))
                .OrderBy(p => p.VersionUtc)
                .LastOrDefault();

            var toAdd = !equalityComparer.Equals(pfe, lastVersion); //if the last version of the PointerFileEntry is not equal -- insert a new one

            if (toAdd)
            {
                await base.AppendAsync(pfe);

                //Ensure the version is in the SORTED master list
                var versions = await versionsTask.Task;
                if (!versions.Contains(pfe.VersionUtc))
                    lock (versions)
                        versions.Add(pfe.VersionUtc);
            }

            return toAdd;
        }


        /// <summary>
        /// Get the versions in universal time
        /// </summary>
        /// <returns></returns>
        public async Task<IEnumerable<DateTime>> GetVersionsAsync()
        {
            return await versionsTask.Task;
        }


    }
}
