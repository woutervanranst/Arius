using Arius.Core.Extensions;
using Arius.Core.Models;
using Arius.Core.Services;
using Azure.Storage.Blobs;
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
                entriesTask = Task.Run(async () =>
                {
                    //var x = container
                    //    .GetBlobs(prefix: $"{PointerFileEntriesFolderName}/")
                    //    .AsParallel()
                    //    .Select(async (bi) =>
                    //    {
                    //        var bc = container.GetBlobClient(bi.Name);

                    //        using var encryptedVersionBlobStream = await bc.OpenReadAsync();
                    //        using var versionMemoryStream = new MemoryStream();

                    //        await CryptoService.DecryptAndDecompressAsync(encryptedVersionBlobStream, versionMemoryStream, passphrase);
                    //        var version = await JsonSerializer.DeserializeAsync<IEnumerable<PointerFileEntry>>(versionMemoryStream);

                    //        return version;

                    //        //versions.AddRange(version.PointerFileEntries);

                    //        //return Task.FromResult(5);

                    //        //var version = DateTime.Parse(bi.Name, System.Globalization.DateTimeStyles.RoundtripKind);
                    //    });

                    //var z = (await Task.WhenAll(x)).SelectMany(x => x).ToList();

                    var entries = new ConcurrentBag<PointerFileEntry>();

                    await Parallel.ForEachAsync(container.GetBlobsAsync(prefix: $"{PointerFileEntriesFolderName}/"), async (bi, ct) =>
                    {
                        var bc = container.GetBlobClient(bi.Name);

                        using var encryptedVersionBlobStream = await bc.OpenReadAsync(cancellationToken: ct);
                        using var versionMemoryStream = new MemoryStream();
                        
                        await CryptoService.DecryptAndDecompressAsync(encryptedVersionBlobStream, versionMemoryStream, passphrase);
                        var pfes = await JsonSerializer.DeserializeAsync<IEnumerable<PointerFileEntry>>(versionMemoryStream, cancellationToken: ct);

                        entries.AddFromEnumerable(pfes);

                        //var version = DateTime.Parse(bi.Name, System.Globalization.DateTimeStyles.RoundtripKind);
                    });

                    versions.SetResult(new ConcurrentHashSet<DateTime>(entries.Select(pfe => pfe.VersionUtc).Distinct()));

                    return entries;
                });
            }

            private readonly ILogger logger;
            private readonly string passphrase;
            private readonly BlobContainerClient container;
            private const string PointerFileEntriesFolderName = "pointerfileentries";
            private readonly Task<ConcurrentBag<PointerFileEntry>> entriesTask;
            private readonly TaskCompletionSource<ConcurrentHashSet<DateTime>> versions = new();
            private readonly static PointerFileEntryEqualityComparer equalityComparer = new();



            private void EnsureVersionOpen(/*DateTime newVersionUtc*/)
            {
                //if (currentVersion is not null)
                //    throw new InvalidOperationException("A version is already open");

                //currentVersion = newVersionUtc;

                if (currentVersionFile is null)
                    currentVersionFile = Path.GetTempFileName();
            }

            //private DateTime? currentVersion;
            private string currentVersionFile;

            public async Task CommitCurrentVersion()
            {
                //if (this.version is null)
                //    throw new InvalidOperationException()
                //var blobName = version.
                //var bc = container.GetBlobClient(bi.Name);

            }



            public async Task<bool> CreatePointerFileEntryIfNotExistsAsync(PointerFileEntry pfe)
            {
                //if (this.currentVersionFile is null)
                //    throw new InvalidOperationException("No version is open");
                EnsureVersionOpen();

                var entries = await entriesTask;

                var lastVersion = entries.AsParallel()
                    .Where(p => pfe.RelativeName.Equals(p.RelativeName))
                    .OrderBy(p => p.VersionUtc)
                    .LastOrDefault();

                var toAdd = !equalityComparer.Equals(pfe, lastVersion); //if the last version of the PointerFileEntry is not equal -- insert a new one

                if (toAdd)
                {
                    // Commit to disk
                    var json = JsonSerializer.Serialize(pfe);
                    await File.AppendAllLinesAsync(currentVersionFile, json.SingleToArray()); // alternates: https://stackoverflow.com/a/19691606/1582323

                    // Commit to memory (Insert the new PointerFileEntry)
                    entries.Add(pfe);

                    //Ensure the version is in the master list
                    var versions = await this.versions.Task;
                    versions.TryAdd(pfe.VersionUtc); //TODO: aan het einde?
                }

                return toAdd;
            }

            public async Task<IReadOnlyCollection<PointerFileEntry>> GetEntriesAsync()
            {
                var entries = await entriesTask;

                return entries;
            }

            public async Task<IEnumerable<DateTime>> GetVersionsAsync()
            {
                //var versions = await entriesTask;

                //return versions.Select(pfe => pfe.VersionUtc).Distinct(); //TODO optimize?

                return (await versions.Task).Values;
            }





            


            


            

            

            //private readonly struct kakak
            //{
            //    public DateTime VersionUtc { get; init; }
            //    public List<PointerFileEntry> PointerFileEntries  { get; init; }
            //}
        }
    }
}
