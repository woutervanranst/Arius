﻿using System;
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

                // Is created in ChunkRepository
            }

            private readonly ILogger<ManifestRepository> _logger;
            private readonly BlobContainerClient _bcc;

            internal const string ManifestDirectoryName = "manifests";

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

            public IEnumerable<ManifestBlob> GetAllManifestBlobs()
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

            public IEnumerable<HashValue> GetAllManifestHashes()
            {
                return GetAllManifestBlobs().Select(mb => mb.Hash).ToArray();
            }

            public async Task<IEnumerable<HashValue>> GetChunkHashesAsync(HashValue manifestHash)
            {
                _logger.LogInformation($"Getting chunks for manifest {manifestHash.Value}");
                var chunks = Array.Empty<HashValue>();

                try
                {
                    var bc = _bcc.GetBlobClient($"{ManifestDirectoryName}/{manifestHash}");

                    var ms = new MemoryStream();
                    await bc.DownloadToAsync(ms);
                    var bytes = ms.ToArray();
                    var json = Encoding.UTF8.GetString(bytes);
                    chunks = JsonSerializer.Deserialize<IEnumerable<string>>(json)!.Select(hv => new HashValue() { Value = hv }).ToArray();

                    return chunks;
                }
                catch (Azure.RequestFailedException e) when (e.ErrorCode == "BlobNotFound")
                {
                    throw new InvalidOperationException("Manifest does not exist");
                }
                finally
                {
                    _logger.LogInformation($"Getting chunks for manifest {manifestHash.Value}... found {chunks.Length} chunk(s)");
                }
            }
        }
    }
}