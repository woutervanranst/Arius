using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Arius.Core.Models;
using Arius.Core.Services;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Microsoft.Extensions.Logging;

namespace Arius.Core.Repositories
{
    internal partial class Repository
    {
        internal const string ChunkFolderName = "chunks";
        internal const string RehydratedChunkFolderName = "chunks-rehydrated";

        
        // GET

        public ChunkBlobBase[] GetAllChunkBlobs()
        {
            logger.LogInformation($"Getting all ChunkBlobs...");
            var r = Array.Empty<ChunkBlobBase>();

            try
            {
                return r = container.GetBlobs(prefix: $"{ChunkFolderName}/")
                    .Select(bi => new ChunkBlobItem(bi))
                    .ToArray();
            }
            finally
            {
                logger.LogInformation($"Getting all ChunkBlobs... got {r.Length}");
            }
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

        private string GetChunkBlobName(string folder, ChunkHash chunkHash) => GetChunkBlobFullName(folder, $"{chunkHash.Value}{ChunkBlobBase.Extension}");
        private string GetChunkBlobFullName(string folder, string name) => $"{folder}/{name}";

        /// <summary>
        /// Get a ChunkBlobBase in the given folder with the given name.
        /// Return null if it doesn't exist.
        /// </summary>
        /// <param name="folder"></param>
        /// <param name="name"></param>
        /// <returns></returns>
        internal ChunkBlobBase GetChunkBlobByName(string folder, string name) => GetChunkBlobByName(GetChunkBlobFullName(folder, name));

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

        public async Task<bool> ChunkExistsAsync(ChunkHash chunkHash)
        {
            return await container.GetBlobClient(GetChunkBlobName(ChunkFolderName, chunkHash)).ExistsAsync();
        }


        // HYDRATE

        public async Task HydrateChunkAsync(ChunkBlobBase blobToHydrate)
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

                var status = hydratedItem.GetProperties().Value.ArchiveStatus;
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
        public async Task<long> UploadChunkAsync(IChunk chunk, AccessTier tier)
        {
            var bbc = container.GetBlockBlobClient(GetChunkBlobName(ChunkFolderName, chunk.Hash));

            if (await bbc.ExistsAsync())
                throw new InvalidOperationException(); //TODO combine with OpenWriteAsync? //TODO gracefully?

            try
            {
                // v11 [DEPRECATED] of storage SDK with PutBlock: https://www.andrewhoefling.com/Blog/Post/uploading-large-files-to-azure-blob-storage-in-c-sharp
                // v12 with blockBlob.Upload: https://blog.matrixpost.net/accessing-azure-storage-account-blobs-from-c-applications/

                long length;
                using (var ss = await chunk.OpenReadAsync())
                {
                    using (var ts = await bbc.OpenWriteAsync(true))
                    {
                        await CryptoService.CompressAndEncryptAsync(ss, ts, passphrase);
                        length = ts.Position;
                    }
                }

                await bbc.SetAccessTierAsync(tier);

                return length;
            }
            catch (Exception e)
            {
                logger.LogError(e, "Error while uploading chunk. Deleting potentially corrupt chunk...", chunk, tier); //TODO test this in a unit test
                
                await bbc.DeleteAsync();

                logger.LogInformation("Error while uploading chunk. Deleting potentially corrupt chunk... Success.");
                throw;
            }
        }

        public async Task DownloadChunkAsync(ChunkBlobBase cbb, FileInfo target)
        {
            try
            {
                using (var ts = target.Create())
                {
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