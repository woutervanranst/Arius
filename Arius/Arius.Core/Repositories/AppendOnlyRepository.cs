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
    internal class AppendOnlyRepository<T>
    {
        public AppendOnlyRepository(ILogger<AppendOnlyRepository<T>> logger, IOptions options, BlobContainerClient container, string folderName)
        {
            this.logger = logger;
            this.passphrase = options.Passphrase;
            this.container = container;
            this.folderName = folderName;

            //Start loading all entries
            itemsTask = Task.Run(LoadItemsAsync);

            // Initialize commit queue
            itemsToCommit = Channel.CreateBounded<T>(new BoundedChannelOptions(ENTRIES_PER_BATCH * 2) { FullMode = BoundedChannelFullMode.Wait, SingleWriter = false, SingleReader = true });
            commitItemsTask = Task.Run(CommitAllItems);
        }

        private readonly ILogger<AppendOnlyRepository<T>> logger;
        private readonly string passphrase;
        private readonly BlobContainerClient container;
        private readonly string folderName;

        private readonly Task<ConcurrentHashSet<T>> itemsTask;
        private readonly Channel<T> itemsToCommit;
        private readonly Task commitItemsTask;

        private const int ENTRIES_PER_BATCH = 1_000;


        private async Task<ConcurrentHashSet<T>> LoadItemsAsync()
        {
            var r = new ConcurrentHashSet<T>();

            await Parallel.ForEachAsync(container.GetBlobsAsync(prefix: $"{folderName}/"), async (bi, ct) =>
            {
                var bc = container.GetBlobClient(bi.Name);

                await using var ss = await bc.OpenReadAsync(cancellationToken: ct);
                await using var ts = new MemoryStream();

                await CryptoService.DecryptAndDecompressAsync(ss, ts, passphrase);
                ts.Seek(0, SeekOrigin.Begin);

                var items = (await JsonSerializer.DeserializeAsync<IEnumerable<T>>(ts, cancellationToken: ct)).ToArray();

                foreach (var item in items)
                    r.Add(item);

                logger.LogInformation($"Read {items.Length} items from {bi.Name}");
            });

            logger.LogInformation($"Read {r.Count()} items in total");

            return r;
        }

        public async Task<IEnumerable<T>> GetAllItemsAsync() => await itemsTask;

        public async Task AddAsync(T item)
        {
            // Insert the item into the memory list
            var entries = await itemsTask;
            entries.Add(item);

            // Queue to commit to write to blob storage
            await itemsToCommit.Writer.WriteAsync(item);
        }


        private async Task CommitAllItems()
        {
            var batch = new List<T>();

            await foreach (var item in itemsToCommit.Reader.ReadAllAsync())
            {
                batch.Add(item);

                if (batch.Count >= ENTRIES_PER_BATCH ||
                    (itemsToCommit.Reader.Completion.IsCompleted && itemsToCommit.Reader.Count == 0))
                { 
                    //Commit this block
                    await CommitBatch(batch.ToArray());
                    batch.Clear();
                }
            }

            // Commit the last block
            await CommitBatch(batch.ToArray());
        }

        private async Task CommitBatch(T[] batch)
        {
            if (!batch.Any()) //in case the last batch does not contain any elements
                return;

            var bbc = container.GetBlockBlobClient($"{folderName}/{DateTime.UtcNow:s}-{Guid.NewGuid()}"); //get a unique blob name

            await using var ts = await bbc.OpenWriteAsync(overwrite: true);
            await using var ms = new MemoryStream();

            await JsonSerializer.SerializeAsync(ms, batch);
            ms.Seek(0, SeekOrigin.Begin);

            //using (var temp = File.OpenWrite(Path.GetTempFileName()))
            //{
            //    ms.CopyTo(temp);
            //    ms.Seek(0, SeekOrigin.Begin);
            //}

            await CryptoService.CompressAndEncryptAsync(ms, ts, passphrase);

            await bbc.SetAccessTierAsync(AccessTier.Cool);
        }

        /// <summary>
        /// Mark the repository as closed and write the remaining entries to storage
        /// </summary>
        /// <returns></returns>
        public async Task Commit()
        {
            itemsToCommit.Writer.Complete();

            await commitItemsTask;
        }
    }
}
