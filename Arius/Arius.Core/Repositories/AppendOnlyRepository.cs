using Arius.Core.Extensions;
using Arius.Core.Models;
using Arius.Core.Services;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using ConcurrentCollections;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Arius.Core.Repositories;

internal partial class Repository
{
    internal abstract class AppendOnlyRepository<T>
    {
        protected AppendOnlyRepository(ILogger logger, IOptions options, BlobContainerClient container)
        {
            this.logger = logger;
            this.passphrase = options.Passphrase;
            this.container = container;

            //Start loading all entries
            entriesTask = Task.Run(() => LoadEntries(container));

            // Initialize commit queue
            entriesToCommit = Channel.CreateBounded<PointerFileEntry>(new BoundedChannelOptions(ENTRIES_PER_FILE * 2) { FullMode = BoundedChannelFullMode.Wait, SingleWriter = false, SingleReader = true });
            processEntriesToCommitTask = Task.Run(() => ProcessEntriesToCommit());
        }

        private readonly ILogger logger;
        private readonly string passphrase;
        private readonly BlobContainerClient container;
        private const string PointerFileEntriesFolderName = "pointerfileentries";
        private readonly Task<ConcurrentHashSet<PointerFileEntry>> entriesTask;
        private readonly Channel<PointerFileEntry> entriesToCommit;
        private readonly TaskCompletionSource<SortedSet<DateTime>> versionsTask = new();
        private readonly static PointerFileEntryEqualityComparer equalityComparer = new();
        private const int ENTRIES_PER_FILE = 3; //1_000;
        private readonly Task processEntriesToCommitTask;


        private async Task<ConcurrentHashSet<PointerFileEntry>> LoadEntries(BlobContainerClient container)
        {
            var r = new ConcurrentHashSet<PointerFileEntry>();

            await Parallel.ForEachAsync(container.GetBlobsAsync(prefix: $"{PointerFileEntriesFolderName}/"), async (bi, ct) =>
            {
                var bc = container.GetBlobClient(bi.Name);

                using var ss = await bc.OpenReadAsync(cancellationToken: ct);
                using var ts = new MemoryStream();

                await CryptoService.DecryptAndDecompressAsync(ss, ts, passphrase);
                ts.Seek(0, SeekOrigin.Begin);

                var pfes = await JsonSerializer.DeserializeAsync<IEnumerable<PointerFileEntry>>(ts, cancellationToken: ct);

                foreach (var pfe in pfes)
                    r.Add(pfe);
            });

            versionsTask.SetResult(new SortedSet<DateTime>(r.Select(pfe => pfe.VersionUtc).Distinct()));

            return r;
        }

        /// <summary>
        /// Insert the PointerFileEntry into the table storage, if a similar entry (according to the PointerFileEntryEqualityComparer) does not yet exist
        /// </summary>
        /// <param name="pfe"></param>
        public async Task<bool> CreatePointerFileEntryIfNotExistsAsync(PointerFileEntry pfe)
        {
            var entries = await entriesTask;

            var lastVersion = entries.AsParallel()
                .Where(p => pfe.RelativeName.Equals(p.RelativeName))
                .OrderBy(p => p.VersionUtc)
                .LastOrDefault();

            var toAdd = !equalityComparer.Equals(pfe, lastVersion); //if the last version of the PointerFileEntry is not equal -- insert a new one

            if (toAdd)
            {
                await entriesToCommit.Writer.WriteAsync(pfe);

                //Insert the new PointerFileEntry
                entries.Add(pfe);

                //Ensure the version is in the SORTED master list
                var versions = await versionsTask.Task;
                if (!versions.Contains(pfe.VersionUtc))
                    lock (versions)
                        versions.Add(pfe.VersionUtc);
            }

            return toAdd;
        }

        public async Task<IEnumerable<PointerFileEntry>> GetEntriesAsync()
        {
            var existingEntries = await entriesTask;

            return existingEntries;
        }

        /// <summary>
        /// Get the versions in universal time
        /// </summary>
        /// <returns></returns>
        public async Task<IEnumerable<DateTime>> GetVersionsAsync()
        {
            return await versionsTask.Task;
        }




        private async Task ProcessEntriesToCommit()
        {
            var pfes = new List<PointerFileEntry>();

            await foreach (var pfe in entriesToCommit.Reader.ReadAllAsync())
            {
                pfes.Add(pfe);

                if (pfes.Count >= ENTRIES_PER_FILE)
                { 
                    //Commit this block
                    await Emit(pfes);
                    pfes.Clear();
                }
            }

            // Commit the last block
            await Emit(pfes);
        }

        private async Task Emit(IEnumerable<PointerFileEntry> pfes)
        {
            if (!pfes.Any())
                return;

            //var bbc = container.GetBlockBlobClient($"{PointerFileEntriesFolderName}/{DateTime.UtcNow.Ticks}");
            var bbc = container.GetBlockBlobClient($"{PointerFileEntriesFolderName}/{DateTime.UtcNow:s}-{Guid.NewGuid().GetHashCode()}"); //get a unique blob name

            using var ts = await bbc.OpenWriteAsync(overwrite: true);
            using var ms = new MemoryStream();

            await JsonSerializer.SerializeAsync(ms, pfes);
            ms.Seek(0, SeekOrigin.Begin);

            //using (var temp = File.OpenWrite(Path.GetTempFileName()))
            //{
            //    ms.CopyTo(temp);
            //    ms.Seek(0, SeekOrigin.Begin);
            //}

            await CryptoService.CompressAndEncryptAsync(ms, ts, passphrase);

            await bbc.SetAccessTierAsync(AccessTier.Cool);
        }

        public async Task CommitPointerFileVersion()
        {
            entriesToCommit.Writer.Complete();

            await processEntriesToCommitTask;
        }
    }
}
