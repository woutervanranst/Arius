using System;
using System.Collections.Generic;
using System.IO;
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

        private void InitManifestRepository()
        {
            // 'Partial constructor' for this part of the repo
        }


        // GET

        public ManifestBlob[] GetAllManifestBlobs()
        {
            logger.LogInformation($"Getting all manifests...");
            var r = Array.Empty<ManifestBlob>();

            try
            {
                return r = _bcc.GetBlobs(prefix: $"{ManifestDirectoryName}/")
                    .Where(bi => !bi.Name.EndsWith(".manifest.7z.arius")) //back compat for v4 archives
                    .Select(bi => new ManifestBlob(bi))
                    .ToArray();
            }
            finally
            {
                logger.LogInformation($"Getting all manifests... got {r.Length}");
            }
        }

        public HashValue[] GetAllManifestHashes()
        {
            return GetAllManifestBlobs().Select(mb => mb.Hash).ToArray();
        }

        public async Task<bool> ManifestExistsAsync(HashValue manifestHash)
        {
            return await _bcc.GetBlobClient(GetManifestBlobName(manifestHash)).ExistsAsync();
        }

        private string GetManifestBlobName(HashValue manifestHash) => $"{ManifestDirectoryName}/{manifestHash}";

        public async Task<HashValue[]> GetChunkHashesForManifestAsync(HashValue manifestHash)
        {
            logger.LogInformation($"Getting chunks for manifest {manifestHash.Value}");
            var chunkHashes = Array.Empty<HashValue>();

            try
            {
                var bc = _bcc.GetBlobClient(GetManifestBlobName(manifestHash));

                var ms = new MemoryStream();
                await bc.DownloadToAsync(ms);
                var bytes = ms.ToArray();
                var json = Encoding.UTF8.GetString(bytes);
                chunkHashes = JsonSerializer.Deserialize<IEnumerable<string>>(json)!.Select(hv => new HashValue() { Value = hv }).ToArray();

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

        public async Task AddManifestAsync(BinaryFile binaryFile, IChunkFile[] chunkFiles)
        {
            logger.LogInformation($"Creating manifest for {binaryFile.RelativeName}");

            await AddManifestAsync(binaryFile.Hash, chunkFiles.Select(cf => cf.Hash).ToArray());

            logger.LogInformation($"Creating manifest for {binaryFile.RelativeName}... done");
        }
        public async Task AddManifestAsync(HashValue manifestHash, HashValue[] chunkHashes)
        {
            try
            {
                var bc = _bcc.GetBlobClient(GetManifestBlobName(manifestHash));

                if (bc.Exists())
                    throw new InvalidOperationException("Manifest Already Exists");

                var json = JsonSerializer.Serialize(chunkHashes.Select(cf => cf.Value));
                var bytes = Encoding.UTF8.GetBytes(json);
                var ms = new MemoryStream(bytes);

                await bc.UploadAsync(ms, new BlobUploadOptions { AccessTier = AccessTier.Cool });
            }
            catch (Exception)
            {
                throw;
            }

        }
    }
}