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
        protected AppendOnlyRepository(ILogger logger, IOptions options, BlobContainerClient container, string folderName)
        {
            this.logger = logger;
            this.passphrase = options.Passphrase;
            this.container = container;
            this.folderName = folderName;

            //Start loading all entries
            entriesTask = Task.Run(() => LoadEntriesAsync(container));

            // Initialize commit queue
            entriesToCommit = Channel.CreateBounded<T>(new BoundedChannelOptions(ENTRIES_PER_FILE * 2) { FullMode = BoundedChannelFullMode.Wait, SingleWriter = false, SingleReader = true });
            processEntriesToCommitTask = Task.Run(() => CommitEntriesTask());
        }

        private readonly ILogger logger;
        private readonly string passphrase;
        private readonly BlobContainerClient container;
        private readonly string folderName;

        private readonly Task<ConcurrentHashSet<T>> entriesTask;
        private readonly Channel<T> entriesToCommit;
        private readonly Task processEntriesToCommitTask;

        
        private const int ENTRIES_PER_FILE = 3; //1_000;


        protected virtual async Task<ConcurrentHashSet<T>> LoadEntriesAsync(BlobContainerClient container)
        {
            var r = new ConcurrentHashSet<T>();

            await Parallel.ForEachAsync(container.GetBlobsAsync(prefix: $"{folderName}/"), async (bi, ct) =>
            {
                var bc = container.GetBlobClient(bi.Name);

                using var ss = await bc.OpenReadAsync(cancellationToken: ct);
                using var ts = new MemoryStream();

                await CryptoService.DecryptAndDecompressAsync(ss, ts, passphrase);
                ts.Seek(0, SeekOrigin.Begin);

                var items = await JsonSerializer.DeserializeAsync<IEnumerable<T>>(ts, cancellationToken: ct);

                foreach (var item in items)
                    r.Add(item);
            });

            return r;
        }

        public async Task<IEnumerable<T>> GetEntriesAsync() => await entriesTask;

        protected async Task AppendAsync(T item)
        {
            // Insert the item into the memory list
            var entries = await entriesTask;
            entries.Add(item);

            // Queue to commit to write to blob storage
            await entriesToCommit.Writer.WriteAsync(item);
        }


        private async Task CommitEntriesTask()
        {
            var items = new List<T>();

            await foreach (var item in entriesToCommit.Reader.ReadAllAsync())
            {
                items.Add(item);

                if (items.Count >= ENTRIES_PER_FILE)
                { 
                    //Commit this block
                    await Emit(items);
                    items.Clear();
                }
            }

            // Commit the last block
            await Emit(items);
        }

        private async Task Emit(IEnumerable<T> items)
        {
            if (!items.Any())
                return;

            var bbc = container.GetBlockBlobClient($"{folderName}/{DateTime.UtcNow:s}-{Guid.NewGuid().GetHashCode()}"); //get a unique blob name

            using var ts = await bbc.OpenWriteAsync(overwrite: true);
            using var ms = new MemoryStream();

            await JsonSerializer.SerializeAsync(ms, items);
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
