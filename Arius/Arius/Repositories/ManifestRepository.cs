using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Arius.CommandLine;
using Arius.Models;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Azure.Cosmos.Table;
using Microsoft.Extensions.Logging;

namespace Arius.Repositories
{
    internal partial class AzureRepository
    {
        private class ManifestRepository
        {
            public ManifestRepository(ICommandExecutorOptions options, ILogger<ManifestRepository> logger)
            {
                _logger = logger;

                var o = (IAzureRepositoryOptions)options;

                var connectionString = $"DefaultEndpointsProtocol=https;AccountName={o.AccountName};AccountKey={o.AccountKey};EndpointSuffix=core.windows.net";

                var bsc = new BlobServiceClient(connectionString);
                _bcc = bsc.GetBlobContainerClient(o.Container);

                // Is created in ChunkRepository
            }

            private readonly ILogger<ManifestRepository> _logger;
            private readonly BlobContainerClient _bcc;
            private const string ManifestDirectoryName = "manifests";

            public async Task AddManifestAsync(BinaryFile bf, IChunkFile[] cfs)
            {
                try
                {
                    _logger.LogInformation($"Creating manifest for {bf.RelativeName}");

                    var bc = _bcc.GetBlobClient($"{ManifestDirectoryName}/{bf.Hash}");

                    if (bc.Exists())
                        throw new InvalidOperationException("Manifest Already Exists");

                    var json = JsonSerializer.Serialize(cfs.Select(cf => cf.Hash.Value));
                    var bytes = Encoding.UTF8.GetBytes(json);
                    var ms = new MemoryStream(bytes);

                    await bc.UploadAsync(ms, new BlobUploadOptions { AccessTier = AccessTier.Cool });

                    _logger.LogInformation($"Creating manifest for {bf.RelativeName}... done");
                }
                catch (Exception)
                {
                    throw;
                }
            }

            public IEnumerable<HashValue> GetAllManifestHashes()
            {
                _logger.LogInformation($"Getting all manifests...");

                try
                {
                    return _bcc.GetBlobs(prefix: ($"{ManifestDirectoryName}/"))
                        .Where(bi => !bi.Name.EndsWith(".manifest.7z.arius")) //back compat for v4 archives
                        .Select(bi => new RemoteManifestBlobItem(bi).Hash).ToArray();
                }
                finally
                {
                    _logger.LogInformation($"Getting all manifests... done"); // TODO logging in the final?
                }
            }

            public async Task<IEnumerable<HashValue>> GetChunkHashesAsync(HashValue manifestHash)
            {
                try
                {
                    _logger.LogInformation($"Getting chunks for manifest {manifestHash.Value}");

                    var bc = _bcc.GetBlobClient($"{ManifestDirectoryName}/{manifestHash}");

                    if (!bc.Exists())
                        throw new InvalidOperationException("Manifest does not exist");

                    var ss = new MemoryStream();
                    var r = await bc.DownloadToAsync(ss);
                    var bytes = ss.ToArray();
                    var json = Encoding.UTF8.GetString(bytes);
                    var chunks = JsonSerializer.Deserialize<IEnumerable<string>>(json)!.Select(hv => new HashValue() { Value = hv }).ToArray();

                    _logger.LogInformation($"Getting chunks for manifest {manifestHash.Value}... found {chunks.Length} chunk(s)");

                    return chunks;
                }
                finally
                {
                }
            }
        }
    }
}