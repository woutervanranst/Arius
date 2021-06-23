using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Arius.Core.Commands;
using Arius.Core.Models;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;

namespace Arius.Core.Repositories
{
    internal partial class AzureRepository
    {
        internal class ManifestRepository
        {
            public ManifestRepository(IOptions options, ILogger<ManifestRepository> logger)
            {
                _logger = logger;

                var connectionString = $"DefaultEndpointsProtocol=https;AccountName={options.AccountName};AccountKey={options.AccountKey};EndpointSuffix=core.windows.net";

                var bsc = new BlobServiceClient(connectionString);
                _bcc = bsc.GetBlobContainerClient(options.Container);
            }

            private readonly ILogger<ManifestRepository> _logger;
            private readonly BlobContainerClient _bcc;

            internal const string ManifestDirectoryName = "manifests";

            // GET

            public ManifestBlob[] GetAllManifestBlobs()
            {
                _logger.LogInformation($"Getting all manifests...");
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
                    _logger.LogInformation($"Getting all manifests... got {r.Length}");
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
                _logger.LogInformation($"Getting chunks for manifest {manifestHash.Value}");
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
                    _logger.LogInformation($"Getting chunks for manifest {manifestHash.Value}... found {chunkHashes.Length} chunk(s)");
                }
            }


            // ADD
            
            public async Task AddManifestAsync(BinaryFile binaryFile, IChunkFile[] chunkFiles)
            {
                _logger.LogInformation($"Creating manifest for {binaryFile.RelativeName}");

                await AddManifestAsync(binaryFile.Hash, chunkFiles.Select(cf => cf.Hash).ToArray());

                _logger.LogInformation($"Creating manifest for {binaryFile.RelativeName}... done");
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
}