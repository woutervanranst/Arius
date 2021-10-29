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
using System.Threading.Tasks;

namespace Arius.Core.Repositories
{
    internal partial class Repository
    {
        internal class CachedEncryptedPointerFileEntryRepository2
        {
            public CachedEncryptedPointerFileEntryRepository2(ILogger<CachedEncryptedPointerFileEntryRepository2> logger, IOptions options, BlobContainerClient container)
            {
                this.logger = logger;
                this.passphrase = options.Passphrase;
                this.container = container;

                //Start loading all entries
                existingEntriesTask = Task.Run(async () =>
                {
                    var chs = new ConcurrentHashSet<PointerFileEntry>();

                    await Parallel.ForEachAsync(container.GetBlobsAsync(prefix: $"{PointerFileEntriesFolderName}/"), async (bi, ct) =>
                    {
                        var bc = container.GetBlobClient(bi.Name);

                        using var ss = await bc.OpenReadAsync(cancellationToken: ct);
                        using var ts = new MemoryStream();
                        
                        await CryptoService.DecryptAndDecompressAsync(ss, ts, passphrase);
                        ts.Seek(0, SeekOrigin.Begin);

                        var pfes = await JsonSerializer.DeserializeAsync<IEnumerable<PointerFileEntry>>(ts, cancellationToken: ct);

                        foreach (var pfe in pfes)
                            chs.Add(pfe);
                    });

                    versions.SetResult(new ConcurrentHashSet<DateTime>(chs.Select(pfe => pfe.VersionUtc).Distinct()));

                    return chs;
                });
            }

            private readonly ILogger logger;
            private readonly string passphrase;
            private readonly BlobContainerClient container;
            private const string PointerFileEntriesFolderName = "pointerfileentries";
            private readonly Task<ConcurrentHashSet<PointerFileEntry>> existingEntriesTask;
            private readonly ConcurrentBag<PointerFileEntry> newEntries = new();
            private readonly TaskCompletionSource<ConcurrentHashSet<DateTime>> versions = new();
            private readonly static PointerFileEntryEqualityComparer equalityComparer = new();

            public async Task CommitPointerFileVersion()
            {
                if (newEntries.IsEmpty)
                    return;

                var bbc = container.GetBlockBlobClient($"{PointerFileEntriesFolderName}/{DateTime.UtcNow.Ticks}");

                using var ts = await bbc.OpenWriteAsync(overwrite: true);
                using var ms = new MemoryStream();

                await JsonSerializer.SerializeAsync(ms, newEntries);
                ms.Seek(0, SeekOrigin.Begin);

                //using (var temp = File.OpenWrite(Path.GetTempFileName()))
                //{
                //    ms.CopyTo(temp);
                //    ms.Seek(0, SeekOrigin.Begin);
                //}

                await CryptoService.CompressAndEncryptAsync(ms, ts, passphrase);

                await bbc.SetAccessTierAsync(AccessTier.Cool);
            }



            public async Task<bool> CreatePointerFileEntryIfNotExistsAsync(PointerFileEntry pfe)
            {
                var existingEntries = await existingEntriesTask;

                var lastVersion = existingEntries.Union(newEntries).AsParallel()
                    .Where(p => pfe.RelativeName.Equals(p.RelativeName))
                    .OrderBy(p => p.VersionUtc)
                    .LastOrDefault();

                var toAdd = !equalityComparer.Equals(pfe, lastVersion); //if the last version of the PointerFileEntry is not equal -- insert a new one

                if (toAdd)
                {
                    //Insert the new PointerFileEntry
                    newEntries.Add(pfe);

                    //Ensure the version is in the master list
                    var versions = await this.versions.Task;
                    versions.Add(pfe.VersionUtc); //TODO: aan het einde?
                }

                return toAdd;
            }

            public async Task<IEnumerable<PointerFileEntry>> GetEntriesAsync()
            {
                var existingEntries = await existingEntriesTask;

                return existingEntries.Union(newEntries);
            }

            public async Task<IEnumerable<DateTime>> GetVersionsAsync()
            {
                return await versions.Task;
            }
        }
    }
}
