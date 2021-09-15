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
        /// Get the count of (distinct) BinaryHashes
        /// </summary>
        /// <returns></returns>
        public async Task<int> GetManifestCountAsync()
        {
            var hs = await GetAllBinaryHashesAsync();

            return hs.Count();
        }

        /// <summary>
        /// Get all the (distinct) BinaryHashes
        /// </summary>
        /// <returns></returns>
        public async Task<IEnumerable<BinaryHash>> GetAllBinaryHashesAsync()
        {
            var pfes = await GetPointerFileEntriesAsync();

            return pfes.Select(pfe => pfe.BinaryHash).Distinct();
        }

        public async Task<bool> ManifestExistsAsync(BinaryHash binaryHash)
        {
            var hs = await GetAllBinaryHashesAsync();

            return hs.Any(h => h == binaryHash);

            //return await container.GetBlobClient(GetManifestBlobName(manifestHash)).ExistsAsync();
        }

        private string GetManifestBlobName(BinaryHash binaryHash) => $"{ManifestDirectoryName}/{binaryHash.Value}";

        public async Task<ChunkHash[]> GetChunksForBinaryAsync(BinaryHash binaryHash)
        {
            logger.LogInformation($"Getting chunks for binary {binaryHash.Value}");
            var chunkHashes = Array.Empty<ChunkHash>();

            try
            {
                var ms = new MemoryStream();

                var bc = container.GetBlobClient(GetManifestBlobName(binaryHash));

                await bc.DownloadToAsync(ms);
                var bytes = ms.ToArray();
                var json = Encoding.UTF8.GetString(bytes);
                chunkHashes = JsonSerializer.Deserialize<IEnumerable<string>>(json)!.Select(hv => new ChunkHash(hv)).ToArray();

                return chunkHashes;
            }
            catch (Azure.RequestFailedException e) when (e.ErrorCode == "BlobNotFound")
            {
                throw new InvalidOperationException($"Manifest '{binaryHash}' does not exist");
            }
            finally
            {
                logger.LogInformation($"Getting chunks for manifest {binaryHash.Value}... found {chunkHashes.Length} chunk(s)");
            }
        }


        // ADD

        //public async Task AddManifestAsync(BinaryFile binaryFile, IChunkFile[] chunkFiles)
        //{
        //    logger.LogInformation($"Creating manifest for {binaryFile.RelativeName}");

        //    await AddManifestAsync(binaryFile.Hash, chunkFiles.Select(cf => cf.Hash).ToArray());

        //    logger.LogInformation($"Creating manifest for {binaryFile.RelativeName}... done");
        //}
        public async Task CreateManifestAsync(BinaryHash binaryHash, ChunkHash[] chunkHashes)
        {
            var bc = container.GetBlobClient(GetManifestBlobName(binaryHash));

            if (bc.Exists())
                throw new InvalidOperationException("Manifest Already Exists");

            var json = JsonSerializer.Serialize(chunkHashes.Select(cf => cf.Value));
            var bytes = Encoding.UTF8.GetBytes(json);
            var ms = new MemoryStream(bytes);

            await bc.UploadAsync(ms, new BlobUploadOptions { AccessTier = AccessTier.Cool });
        }
    }
}