using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Arius.Core.Extensions;
using Arius.Core.Models;
using Arius.Core.Services;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Microsoft.Extensions.Logging;

namespace Arius.Core.Repositories;

internal partial class Repository
{
    public ChunkRepository Chunks { get; init; }

    internal class ChunkRepository
    {
        internal const string ChunkFolderName = "chunks";
        internal const string RehydratedChunkFolderName = "chunks-rehydrated";

        internal ChunkRepository(ILogger<ChunkRepository> logger, Repository parent, BlobContainerClient container, string passphrase)
        {
            this.logger = logger;
            this.parent = parent;
            this.container = container;
            this.passphrase = passphrase;
        }

        private readonly ILogger<ChunkRepository> logger;
        private readonly Repository parent;
        private readonly BlobContainerClient container;
        private readonly string passphrase;

        // GET

        public IAsyncEnumerable<ChunkBlobBase> GetAllChunkBlobs()
        {
            return container.GetBlobsAsync(prefix: $"{ChunkFolderName}/")
                .Select(bi => ChunkBlobBase.GetChunkBlob(container, bi));
        }

        public async Task SetAllAccessTierAsync(AccessTier tier, int maxDegreeOfParallelism = 8)
        {
            if (tier != AccessTier.Archive)
                throw new InvalidOperationException($"Cannot move all chunks to {tier} (costs may explode). Please do this manually.");

            await Parallel.ForEachAsync(GetAllChunkBlobs().Where(cbb => cbb.AccessTier != tier),
                //container.GetBlobsAsync(prefix: $"{ChunkFolderName}/")
                //                            .Where(bi => bi.Properties.AccessTier != tier)
                //                            .Select(bi => container.GetBlobClient(bi.Name)),
                new ParallelOptions { MaxDegreeOfParallelism = maxDegreeOfParallelism },
                async (cbb, ct) =>
                {
                    await cbb.SetAccessTierAsync(tier);
                });
        }

        /// <summary>
        /// Get the RemoteEncryptedChunkBlobItem - either from permanent cold storage or from temporary rehydration storage
        /// If the chunk does not exist, throws an InvalidOperationException
        /// If requireHydrated is true and the chunk does not exist in cold storage, returns null
        /// </summary>
        public ChunkBlobBase GetChunkBlobByHash(ChunkHash chunkHash, bool requireHydrated)
        {
            var blobName = GetChunkBlobName(ChunkFolderName, chunkHash);
            var cb1 = GetChunkBlobByName(blobName);

            if (cb1 is null)
                throw new InvalidOperationException($"No Chunk for hash {chunkHash.Value}");

            // if we don't need a hydrated chunk, return this one
            if (!requireHydrated)
                return cb1;

            // if we require a hydrated chunk, and this one is hydrated, return this one
            if (requireHydrated && cb1.Downloadable)
                return cb1;

            blobName = GetChunkBlobName(RehydratedChunkFolderName, chunkHash);
            var cb2 = GetChunkBlobByName(blobName);

            if (cb2 is null || !cb2.Downloadable)
            {
                // no hydrated chunk exists
                logger.LogDebug($"No hydrated chunk found for {chunkHash}");
                return null;
            }
            else
                return cb2;
        }

        private string GetChunkBlobName(string folder, ChunkHash chunkHash) => GetChunkBlobFullName(folder, chunkHash.Value);
        private string GetChunkBlobFullName(string folder, string name) => $"{folder}/{name}";

        /// <summary>
        /// Get a ChunkBlobBase in the given folder with the given name.
        /// Return null if it doesn't exist.
        /// </summary>
        /// <param name="folder"></param>
        /// <param name="name"></param>
        /// <returns></returns>
        internal ChunkBlobBase GetChunkBlobByName(string folder, string name) => GetChunkBlobByName(GetChunkBlobFullName(folder, name));

        internal ChunkBlobBase GetChunkBlobByName(BlobItem bi) => GetChunkBlobByName(bi.Name);
        /// <summary>
        /// Get a ChunkBlobBase by FullName.
        /// Return null if it doesn't exist.
        /// </summary>
        /// <returns></returns>
        internal ChunkBlobBase GetChunkBlobByName(string blobName)
        {
            try
            {
                var bc = container.GetBlobClient(blobName);
                var cb = ChunkBlobBase.GetChunkBlob(bc);
                return cb;
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        public async Task<bool> ExistsAsync(ChunkHash chunkHash)
        {
            return await container.GetBlobClient(GetChunkBlobName(ChunkFolderName, chunkHash)).ExistsAsync();
        }


        // HYDRATE

        public async Task HydrateAsync(ChunkBlobBase blobToHydrate)
        {
            logger.LogInformation($"Checking hydration for chunk {blobToHydrate.Hash.ToShortString()}");

            if (blobToHydrate.AccessTier == AccessTier.Hot ||
                blobToHydrate.AccessTier == AccessTier.Cool)
                throw new InvalidOperationException($"Calling Hydrate on a blob that is already hydrated ({blobToHydrate.Name})");

            var hydratedItem = container.GetBlobClient($"{RehydratedChunkFolderName}/{blobToHydrate.Name}");

            if (!await hydratedItem.ExistsAsync())
            {
                //Start hydration
                await hydratedItem.StartCopyFromUriAsync(
                    blobToHydrate.Uri,
                    new BlobCopyFromUriOptions { AccessTier = AccessTier.Cool, RehydratePriority = RehydratePriority.Standard });

                logger.LogInformation($"Hydration started for {blobToHydrate.Hash.ToShortString()}");
            }
            else
            {
                // Get hydration status
                // https://docs.microsoft.com/en-us/azure/storage/blobs/storage-blob-rehydration

                var status = (await hydratedItem.GetPropertiesAsync()).Value.ArchiveStatus;
                if (status == "rehydrate-pending-to-cool" || status == "rehydrate-pending-to-hot")
                    logger.LogInformation($"Hydration pending for {blobToHydrate.Hash.ToShortString()}");
                else if (status == null)
                    logger.LogInformation($"Hydration done for {blobToHydrate.Hash.ToShortString()}");
                else
                    throw new ArgumentException("TODO");
            }
        }


        // DELETE

        public async Task DeleteHydrateFolderAsync()
        {
            logger.LogInformation("Deleting temporary hydration folder");

            await foreach (var bi in container.GetBlobsAsync(prefix: RehydratedChunkFolderName))
            {
                var bc = container.GetBlobClient(bi.Name);
                await bc.DeleteAsync();
            }
        }


        // UPLOAD & DOWNLOAD

        /// <summary>
        /// Upload a (plaintext) chunk to the repository after compressing and encrypting it
        /// </summary>
        /// <returns>Returns the length of the uploaded stream.</returns>
        public async Task<long> UploadAsync(IChunk chunk, AccessTier tier)
        {
            logger.LogDebug($"Uploading Chunk {chunk.Hash.ToShortString()}...");

            var bbc = container.GetBlockBlobClient(GetChunkBlobName(ChunkFolderName, chunk.Hash));

            if (await bbc.ExistsAsync())
            {
                var p = (await bbc.GetPropertiesAsync()).Value;
                if (!p.HasMetadataTagAsync(SUCCESSFUL_UPLOAD_METADATA_TAG) || p.ContentLength == 0)
                {
                    logger.LogWarning($"Corrupt chunk {chunk.Hash}. Deleting and uploading again");
                    await bbc.DeleteAsync();
                }
                else
                    throw new InvalidOperationException($"Chunk {chunk.Hash} with nonzero length already exists, but somehow we are uploading this again."); //this would be a multithreading issue
            }

            try
            {
                // v12 with blockBlob.Upload: https://blog.matrixpost.net/accessing-azure-storage-account-blobs-from-c-applications/

                long length;
                using (var ts = await bbc.OpenWriteAsync(overwrite: true))
                {
                    using var ss = await chunk.OpenReadAsync();
                    await CryptoService.CompressAndEncryptAsync(ss, ts, passphrase);
                    length = ts.Position;
                }

                await bbc.SetMetadataTagAsync(SUCCESSFUL_UPLOAD_METADATA_TAG);
                await bbc.SetAccessTierAsync(tier);

                logger.LogInformation($"Uploading Chunk {chunk.Hash.ToShortString()}... done");

                return length;
            }
            catch (Exception e)
            {
                var e2 = new InvalidOperationException($"Error while uploading chunk {chunk.Hash}. Deleting...", e);
                logger.LogError(e2); //TODO test this in a unit test
                await bbc.DeleteAsync();
                logger.LogDebug("Error while uploading chunk. Deleting potentially corrupt chunk... Success.");
                
                throw e2;
            }
        }

        public async Task DownloadAsync(ChunkBlobBase cbb, FileInfo target)
        {
            try
            {
                using (var ts = target.Create())
                {
                    throw new NotImplementedException();
                    //if (!await bbc.HasMetadataTagAsync(SUCCESSFUL_UPLOAD_METADATA_TAG))
                    //    throw new InvalidOperationException($"ChunkList '{bh}' does not have the '{SUCCESSFUL_UPLOAD_METADATA_TAG}' tag and is potentially corrupt");

                    using (var ss = await cbb.OpenReadAsync())
                    {
                        await CryptoService.DecryptAndDecompressAsync(ss, ts, passphrase);
                    }
                }
            }
            catch (Exception e)
            {
                throw; //TODO
            }
        }
    }
}