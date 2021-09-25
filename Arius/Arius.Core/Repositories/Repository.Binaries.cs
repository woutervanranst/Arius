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
        internal const string BinaryManifestFolderName = "binarymanifests";

        // GET

        /// <summary>
        /// Get the count of (distinct) BinaryHashes
        /// </summary>
        /// <returns></returns>
        public async Task<int> GetBinaryCountAsync() //TODO move to test?
        {
            var hs = await GetAllBinaryHashesAsync();

            return hs.Count();
        }
        
        public async Task<bool> BinaryExistsAsync(BinaryHash binaryHash)
        {
            var hs = await GetAllBinaryHashesAsync();

            return hs.Any(h => h == binaryHash);

            //return await container.GetBlobClient(GetManifestBlobName(manifestHash)).ExistsAsync();
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


        public async Task<ChunkHash[]> GetChunksForBinaryAsync(BinaryHash binaryHash)
        {
            logger.LogInformation($"Getting chunks for binary {binaryHash.Value}");
            var chunkHashes = Array.Empty<ChunkHash>();

            try
            {
                var ms = new MemoryStream();

                var bc = container.GetBlobClient(GetBinaryManifestBlobName(binaryHash));

                await bc.DownloadToAsync(ms);
                ms.Position = 0;
                chunkHashes = (await JsonSerializer.DeserializeAsync<IEnumerable<string>>(ms))!.Select(hv => new ChunkHash(hv)).ToArray();

                return chunkHashes;
            }
            catch (Azure.RequestFailedException e) when (e.ErrorCode == "BlobNotFound")
            {
                throw new InvalidOperationException($"BinaryManifest '{binaryHash}' does not exist");
            }
            finally
            {
                logger.LogInformation($"Getting chunks for manifest {binaryHash.Value}... found {chunkHashes.Length} chunk(s)");
            }
        }

        public async Task CreateBinaryManifestAsync(BinaryHash binaryHash, ChunkHash[] chunkHashes)
        {
            var bc = container.GetBlobClient(GetBinaryManifestBlobName(binaryHash));

            if (bc.Exists())
                throw new InvalidOperationException("BinaryManifest Already Exists");

            var json = JsonSerializer.Serialize(chunkHashes.Select(cf => cf.Value)); //TODO as async?
            var bytes = Encoding.UTF8.GetBytes(json);
            var ms = new MemoryStream(bytes);

            await bc.UploadAsync(ms, new BlobUploadOptions { AccessTier = AccessTier.Cool });
        }

        private string GetBinaryManifestBlobName(BinaryHash binaryHash) => $"{BinaryManifestFolderName}/{binaryHash.Value}";

    }
}