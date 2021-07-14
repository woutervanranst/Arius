using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Arius.Core.Models;
using Arius.Core.Services;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using static Arius.Core.Facade.Facade;

namespace Arius.Core.Repositories
{
    internal partial class Repository
    {
        internal const string ChunkDirectoryName = "chunks";
        internal const string RehydratedChunkDirectoryName = "chunks-rehydrated";

        private void InitChunkRepository()
        {
            // 'Partial constructor' for this part of the repo
        }

        // GET

        public ChunkBlobBase[] GetAllChunkBlobs()
        {
            logger.LogInformation($"Getting all ChunkBlobs...");
            var r = Array.Empty<ChunkBlobBase>();

            try
            {
                return r = container.GetBlobs(prefix: $"{ChunkDirectoryName}/")
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
            var blobName = GetChunkBlobName(ChunkDirectoryName, chunkHash);
            var cb1 = GetChunkBlobByName(blobName);

            if (cb1 is null)
                throw new InvalidOperationException($"No Chunk for hash {chunkHash.Value}");

            // if we don't need a hydrated chunk, return this one
            if (!requireHydrated)
                return cb1;

            // if we require a hydrated chunk, and this one is hydrated, return this one
            if (requireHydrated && cb1.Downloadable)
                return cb1;

            blobName = GetChunkBlobName(RehydratedChunkDirectoryName, chunkHash);
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

        public async Task<bool> ChunkExists(ChunkHash chunkHash)
        {
            return await container.GetBlobClient(GetChunkBlobName(ChunkDirectoryName, chunkHash)).ExistsAsync();
        }


        // HYDRATE

        public void HydrateChunk(ChunkBlobBase blobToHydrate)
        {
            logger.LogInformation($"Hydrating chunk {blobToHydrate.Name}");

            if (blobToHydrate.AccessTier == AccessTier.Hot ||
                blobToHydrate.AccessTier == AccessTier.Cool)
                throw new InvalidOperationException($"Calling Hydrate on a blob that is already hydrated ({blobToHydrate.Name})");

            var hydratedItem = container.GetBlobClient($"{RehydratedChunkDirectoryName}/{blobToHydrate.Name}");

            if (!hydratedItem.Exists())
            {
                //Start hydration
                var archiveItem = container.GetBlobClient(blobToHydrate.FullName);
                hydratedItem.StartCopyFromUri(archiveItem.Uri, new BlobCopyFromUriOptions { AccessTier = AccessTier.Cool, RehydratePriority = RehydratePriority.Standard });

                logger.LogInformation($"Hydration started for {blobToHydrate.Name}");
            }
            else
            {
                // Get hydration status
                // https://docs.microsoft.com/en-us/azure/storage/blobs/storage-blob-rehydration

                var status = hydratedItem.GetProperties().Value.ArchiveStatus;
                if (status == "rehydrate-pending-to-cool" || status == "rehydrate-pending-to-hot")
                    logger.LogInformation($"Hydration pending for {blobToHydrate.Name}");
                else if (status == null)
                    logger.LogInformation($"Hydration done for {blobToHydrate.Name}");
                else
                    throw new ArgumentException("TODO");
            }
        }


        // DELETE

        public void DeleteHydrateFolder()
        {
            logger.LogInformation("Deleting temporary hydration folder");

            foreach (var bi in container.GetBlobs(prefix: RehydratedChunkDirectoryName))
            {
                var bc = container.GetBlobClient(bi.Name);
                bc.Delete();
            }
        }


        // UPLOAD & DOWNLOAD

        public ChunkBlobBase Upload()
        public IEnumerable<ChunkBlobBase> Upload(EncryptedChunkFile[] ecfs, AccessTier tier)
        {
            _blobCopier.Upload(ecfs, tier, ChunkDirectoryName, false);

            // Return an IEnumerable - the result may not be needed/materialized by the caller
            return ecfs.Select(ecf =>
            {
                if (GetChunkBlobByName(ChunkDirectoryName, ecf.Name) is var cb)
                    return cb;

                throw new InvalidOperationException($"Sequence contains no elements - could not create {nameof(ChunkBlobItem)} of uploaded chunk {ecf.Hash}");
            });
        }

        public IEnumerable<EncryptedChunkFile> Download(ChunkBlobBase[] chunkBlobs, DirectoryInfo target, bool flatten)
        {
            var downloadedFiles = _blobCopier.Download(chunkBlobs, target, flatten);

            //if (chunkBlobs.Count() != downloadedFiles.Count())
            //    throw new InvalidOperationException("Amount of downloaded files does not match"); //TODO

            if (chunkBlobs.Select(cb => cb.Name).Except(downloadedFiles.Select(f => f.Name)).Any())
                throw new InvalidOperationException("Amount of downloaded files does not match"); //TODO

            // Return an IEnumerable - the result may not be needed/materialized by the caller
            // TODO: eliminate ToArray call()
            return downloadedFiles.Select(fi => new EncryptedChunkFile(fi)).ToArray();

            //return downloadedFiles.Select(fi2=>
            //{
            //    if (new FileInfo(Path.Combine(target.FullName, fi2.Name)) is var fi)
            //        return new EncryptedChunkFile(null, fi, fi2.Hash);

            //    throw new InvalidOperationException($"Sequence contains no element - {fi.FullName} should have been downloaded but isn't found on disk");
            //});
        }
    }






    //class Uploader
    //{
    //    private readonly IBlobCopier.IOptions options;

    //    public Uploader(IBlobCopier.IOptions options)
    //    {
    //        this.options = options;

    //        //var bsc = new BlobServiceClient(connectionString);
    //        //container = bsc.GetBlobContainerClient(options.Container);

    //        //var r = container.CreateIfNotExists(PublicAccessType.None);

    //        //if (r is not null && r.GetRawResponse().Status == (int)HttpStatusCode.Created)
    //        //this.logger.LogInformation($"Created container {options.Container}... ");

    //    }

    //    //private readonly BlobContainerClient container;


    //    public async Task UploadChunkAsync(ReadOnlyMemory<byte> chunk, ChunkHash hash)
    //    {
    //        var connectionString = $"DefaultEndpointsProtocol=https;AccountName={options.AccountName};AccountKey={options.AccountKey};EndpointSuffix=core.windows.net";
    //        //var bsc = new BlobContainerClient(connectionString, options.Container);
    //        //await bsc.UploadBlobAsync(hash.Value.ToString(), new BinaryData(chunk));


    //        var x = new BlobClient(connectionString, options.Container, hash.Value.ToString());
    //        await x.UploadAsync(new BinaryData(chunk), new BlobUploadOptions { AccessTier = AccessTier.Cool, TransferOptions = new StorageTransferOptions { MaximumConcurrency = 16 } });
    //        //x.DownloadTo()
    //        public async Task UploadChunkAsync(Stream s, ManifestHash hash)
    //        {
    //            var connectionString = $"DefaultEndpointsProtocol=https;AccountName={options.AccountName};AccountKey={options.AccountKey};EndpointSuffix=core.windows.net";
    //            var x = new BlobClient(connectionString, options.Container, hash.Value.ToString());
    //            await x.UploadAsync(s, new BlobUploadOptions { AccessTier = AccessTier.Cool, TransferOptions = new StorageTransferOptions { MaximumConcurrency = 16 } });

    //            //var bsc = new BlobContainerClient(connectionString, options.Container);
    //            //await bsc.UploadBlobAsync(hash.Value.ToString(), s);

    //        }
    //    }


    //}
}




