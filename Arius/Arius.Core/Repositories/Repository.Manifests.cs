using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Arius.Core.Models;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;

namespace Arius.Core.Repositories
{
    internal partial class Repository
    {
        internal const string ManifestDirectoryName = "manifests";

        // GET

        /// <summary>
        /// Get the count of (distinct) ManifestHashes
        /// </summary>
        /// <returns></returns>
        public async Task<int> GetManifestCount()
        {
            var hs = await GetAllManifestHashes();

            return hs.Count();
        }

        /// <summary>
        /// Get all the (distinct) ManifestHashes
        /// </summary>
        /// <returns></returns>
        public async Task<IEnumerable<ManifestHash>> GetAllManifestHashes()
        {
            var pfes = await GetPointerFileEntries();

            return pfes.Select(pfe => pfe.ManifestHash).Distinct();
        }

        //public ManifestBlob[] GetAllManifestBlobs()
        //{
        //    logger.LogInformation($"Getting all manifests...");
        //    var r = Array.Empty<ManifestBlob>();

        //    try
        //    {
        //        return r = container.GetBlobs(prefix: $"{ManifestDirectoryName}/")
        //            .Where(bi => !bi.Name.EndsWith(".manifest.7z.arius")) //back compat for v4 archives
        //            .Select(bi => new ManifestBlob(bi))
        //            .ToArray();
        //    }
        //    finally
        //    {
        //        logger.LogInformation($"Getting all manifests... got {r.Length}");
        //    }
        //}

        public async Task<bool> ManifestExistsAsync(ManifestHash manifestHash)
        {
            var hs = await GetAllManifestHashes();

            return hs.Any(h => h == manifestHash);

            //return await container.GetBlobClient(GetManifestBlobName(manifestHash)).ExistsAsync();
        }

        private string GetManifestBlobName(ManifestHash manifestHash) => $"{ManifestDirectoryName}/{manifestHash.Value}";

        public async Task<ChunkHash[]> GetChunkHashesForManifestAsync(ManifestHash manifestHash)
        {
            logger.LogInformation($"Getting chunks for manifest {manifestHash.Value}");
            var chunkHashes = Array.Empty<ChunkHash>();

            try
            {
                var ms = new MemoryStream();

                var bc = container.GetBlobClient(GetManifestBlobName(manifestHash));

                await bc.DownloadToAsync(ms);
                var bytes = ms.ToArray();
                var json = Encoding.UTF8.GetString(bytes);
                chunkHashes = JsonSerializer.Deserialize<IEnumerable<string>>(json)!.Select(hv => new ChunkHash(hv)).ToArray();

                return chunkHashes;
            }
            catch (Azure.RequestFailedException e) when (e.ErrorCode == "BlobNotFound")
            {
                throw new InvalidOperationException("Manifest does not exist");
            }
            finally
            {
                logger.LogInformation($"Getting chunks for manifest {manifestHash.Value}... found {chunkHashes.Length} chunk(s)");
            }
        }


        // ADD

        //public async Task AddManifestAsync(BinaryFile binaryFile, IChunkFile[] chunkFiles)
        //{
        //    logger.LogInformation($"Creating manifest for {binaryFile.RelativeName}");

        //    await AddManifestAsync(binaryFile.Hash, chunkFiles.Select(cf => cf.Hash).ToArray());

        //    logger.LogInformation($"Creating manifest for {binaryFile.RelativeName}... done");
        //}
        public async Task CreateManifestAsync(ManifestHash manifestHash, ChunkHash[] chunkHashes)
        {
            var bc = container.GetBlobClient(GetManifestBlobName(manifestHash));

            if (bc.Exists())
                throw new InvalidOperationException("Manifest Already Exists");

            var json = JsonSerializer.Serialize(chunkHashes.Select(cf => cf.Value));
            var bytes = Encoding.UTF8.GetBytes(json);
            var ms = new MemoryStream(bytes);

            await bc.UploadAsync(ms, new BlobUploadOptions { AccessTier = AccessTier.Cool });
        }
    }
}