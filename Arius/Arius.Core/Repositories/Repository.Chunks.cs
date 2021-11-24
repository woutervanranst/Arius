using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Arius.Core.Extensions;
using Arius.Core.Models;
using Arius.Core.Services;
using Azure;
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
            this.repo = parent;
            this.container = container;
            this.passphrase = passphrase;
        }

        private readonly ILogger<ChunkRepository> logger;
        private readonly Repository repo;
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
                    if (cbb.AccessTier == tier)
                        return;

                    await cbb.SetAccessTierAsync(tier);
                    logger.LogDebug($"Set acces tier to Archive for {cbb.Hash}");
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
                throw new InvalidOperationException($"Could not find Chunk {chunkHash.Value}");

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
            logger.LogDebug($"Checking hydration for chunk {blobToHydrate.Hash.ToShortString()}");

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

                logger.LogInformation($"Hydration started for '{blobToHydrate.Hash.ToShortString()}'");
            }
            else
            {
                // Get hydration status
                // https://docs.microsoft.com/en-us/azure/storage/blobs/storage-blob-rehydration

                var status = (await hydratedItem.GetPropertiesAsync()).Value.ArchiveStatus;
                if (status == "rehydrate-pending-to-cool" || status == "rehydrate-pending-to-hot")
                    logger.LogInformation($"Hydration pending for '{blobToHydrate.Hash.ToShortString()}'");
                else if (status == null)
                    logger.LogInformation($"Hydration done for '{blobToHydrate.Hash.ToShortString()}'");
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
        internal async Task<long> UploadAsync(IChunk chunk, AccessTier tier)
        {
            logger.LogDebug($"Uploading Chunk '{chunk.Hash.ToShortString()}'...");

            BlockBlobClient bbc = container.GetBlockBlobClient(GetChunkBlobName(ChunkFolderName, chunk.Hash));

        RestartUpload:

            try
            {
                // v12 with blockBlob.Upload: https://blog.matrixpost.net/accessing-azure-storage-account-blobs-from-c-applications/

                long length;
                using (var ts = await bbc.OpenWriteAsync(overwrite: true, options: ThrowOnExistOptions)) //NOTE the SDK only supports OpenWriteAsync with overwrite: true, therefore the ThrowOnExistOptions workaround
                {
                    using var ss = await chunk.OpenReadAsync();
                    await CryptoService.CompressAndEncryptAsync(ss, ts, passphrase);
                    length = ts.Position;
                }

                await bbc.SetAccessTierAsync(tier);
                await bbc.SetHttpHeadersAsync(new BlobHttpHeaders { ContentType = CryptoService.ContentType });

                logger.LogInformation($"Uploading Chunk '{chunk.Hash.ToShortString()}'... done");

                return length;
            }
            catch (RequestFailedException rfe) when (rfe.Status == (int)HttpStatusCode.Conflict)
            {
                // The blob already exists
                try
                {
                    var p = (await bbc.GetPropertiesAsync()).Value;
                    if (p.ContentType != CryptoService.ContentType || p.ContentLength == 0)
                    {
                        logger.LogWarning($"Corrupt chunk {chunk.Hash}. Deleting and uploading again");
                        await bbc.DeleteAsync();

                        goto RestartUpload;
                    }
                    else
                    {
                        // graceful handling if the chunk is already uploaded
                        //throw new InvalidOperationException($"Chunk {chunk.Hash} with length {p.ContentLength} and contenttype {p.ContentType} already exists, but somehow we are uploading this again."); //this would be a multithreading issue
                        logger.LogWarning($"A valid Chunk '{chunk.Hash}' already existsted, perhaps in a previous/crashed run?");

                        return p.ContentLength;
                    }
                }
                catch (Exception e)
                {
                    logger.LogError(e, $"Exception while reading properties of chunk {chunk.Hash}");
                    throw;
                }
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

        internal async Task DownloadAsync(ChunkBlobBase cbb, FileInfo target)
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