using System.Linq;
using Arius.Core.Models;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using Arius.Core.Extensions;
using Arius.Core.Services;

namespace Arius.Core.Repositories;

internal partial class Repository
{
    /// <summary>
    /// Get the count of Binaries (by counting the distinct PointerFileEntry BinaryHashes)
    /// </summary>
    public async Task<int> CountBinariesAsync()
    {
        await using var db = GetStateDbContext();
        return await db.PointerFileEntries.Select(pfe => pfe.BinaryHash).Distinct().CountAsync();
    }

    /// <summary>
    /// Check the existence of a Binary (by checking whether a PointerFileEntry with the corresponding hash exists)
    /// </summary>
    public async Task<bool> BinaryExistsAsync(BinaryHash bh)
    {
        await using var db = GetStateDbContext();
        return await db.PointerFileEntries.AnyAsync(pfe => pfe.BinaryHash == bh.Value);
    }

    /// <summary>
    /// Download the given Binary with the specified options.
    /// Start hydration for the chunks if required.
    /// Returns false if the Binary is not yet hydrated
    /// </summary>
    public async Task<bool> TryDownloadBinaryAsync(BinaryHash bh, BinaryFileInfo target, string passphrase, bool startHydrationIfNeeded = true)
    {
        var chunks = await GetChunkListAsync(bh)
            .SelectAwait(async ch => (ChunkHash: ch, HydratedChunkBlob: await GetHydratedChunkBlobAsync(ch)))
            .ToArrayAsync();

        var chunksToHydrate = chunks
            .Where(c => c.HydratedChunkBlob is null);
        //.Select(c => repo.GetChunkBlob(c.ChunkHash));
        if (chunksToHydrate.Any())
        {
            chunksToHydrate = chunksToHydrate.ToArray();
            //At least one chunk is not hydrated so the Binary cannot be downloaded
            logger.LogInformation($"{chunksToHydrate.Count()} chunk(s) for '{bh}' not hydrated. Cannot yet restore.");

            if (startHydrationIfNeeded)
                foreach (var c in chunksToHydrate)
                    //hydrate this chunk
                    await HydrateChunkAsync(c.ChunkHash);

            return false;
        }
        else
        {
            //All chunks are hydrated  so we can restore the Binary
            logger.LogInformation($"Downloading Binary '{bh}' from {chunks.Length} chunk(s)...");

            var p = await GetChunkEntryAsync(bh);
            var stats = await new Stopwatch().GetSpeedAsync(p.ArchivedLength, async () =>
            {
                await using var ts = target.OpenWriteAsync();

                /* Faster version but more code
                //if (chunks.Length == 1)
                //{
                //    await using var cs = await chunks[0].ChunkBlob.OpenReadAsync();
                //    await CryptoService.DecryptAndDecompressAsync(cs, ts, options.Passphrase);
                //}
                //else
                //{
                //    var x = new ConcurrentDictionary<ChunkHash, byte[]>();

                //    var t0 = Task.Run(async () =>
                //    {
                //        await Parallel.ForEachAsync(chunks,
                //            new ParallelOptions() { MaxDegreeOfParallelism = 20 },
                //            async (c, ct) =>
                //            {
                //                await using var ms = new MemoryStream();
                //                await using var cs = await c.ChunkBlob.OpenReadAsync();
                //                await CryptoService.DecryptAndDecompressAsync(cs, ms, options.Passphrase);
                //                if (!x.TryAdd(c.ChunkHash, ms.ToArray()))
                //                    throw new InvalidOperationException();
                //            });
                //    });

                //    var t1 = Task.Run(async () =>
                //    {
                //        foreach (var (ch, _) in chunks)
                //        {
                //            while (!x.ContainsKey(ch))
                //                await Task.Yield();

                //            if (!x.TryRemove(ch, out var buff))
                //                throw new InvalidOperationException();

                //            await ts.WriteAsync(buff);
                //            //await x[ch].CopyToAsync(ts);
                //        }
                //    });

                //    Task.WaitAll(t0, t1);
                //}
                */

                foreach (var (_, cb) in chunks)
                {
                    await using var cs = await cb!.OpenReadAsync();
                    await CryptoService.DecryptAndDecompressAsync(cs, ts, passphrase);
                }
            });

            logger.LogInformation($"Downloading Binary '{bh}' of {p.ArchivedLength.GetBytesReadable()} from {chunks.Length} chunk(s)... Completed in {stats.seconds}s ({stats.MBps} MBps / {stats.Mbps} Mbps)");

            return true;
        }
    }
}