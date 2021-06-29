using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Arius.Core.Commands;
using Arius.Core.Models;
using Arius.Core.Services;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using static Arius.Core.Facade.Facade;

namespace Arius.Core.Repositories
{
    internal partial class AzureRepository
    {
        internal class ChunkRepository
        {
            public ChunkRepository(IOptions options, ILogger<ChunkRepository> logger, IBlobCopier b)
            {
                _logger = logger;
                _blobCopier = b;

                var connectionString = $"DefaultEndpointsProtocol=https;AccountName={options.AccountName};AccountKey={options.AccountKey};EndpointSuffix=core.windows.net";
                
                var bsc = new BlobServiceClient(connectionString);
                _bcc = bsc.GetBlobContainerClient(options.Container);
                var r = _bcc.CreateIfNotExists(PublicAccessType.None);
                
                if (r is not null && r.GetRawResponse().Status == 201) // Created
                    _logger.LogInformation($"Created container {options.Container}... ");
            }

            private readonly ILogger<ChunkRepository> _logger;
            private readonly IBlobCopier _blobCopier;
            private readonly BlobContainerClient _bcc;

            internal const string EncryptedChunkDirectoryName = "chunks";
            internal const string RehydrationDirectoryName = "chunks-rehydrated";

            // GET

            public ChunkBlobBase[] GetAllChunkBlobs()
            {
                _logger.LogInformation($"Getting all ChunkBlobs...");
                var r = Array.Empty<ChunkBlobBase>();

                try
                {
                    return r = _bcc.GetBlobs(prefix: $"{EncryptedChunkDirectoryName}/")
                        .Select(bi => new ChunkBlobItem(bi))
                        .ToArray();
                }
                finally
                {
                    _logger.LogInformation($"Getting all ChunkBlobs... got {r.Length}");
                }
            }


            /// <summary>
            /// Get the RemoteEncryptedChunkBlobItem - either from permanent cold storage or from temporary rehydration storage
            /// If the chunk does not exist, throws an InvalidOperationException
            /// If requireHydrated is true and the chunk does not exist in cold storage, returns null
            /// </summary>
            public ChunkBlobBase GetChunkBlobByHash(HashValue chunkHash, bool requireHydrated)
            {
                var blobName = GetChunkBlobName(EncryptedChunkDirectoryName, chunkHash);
                var cb1 = GetChunkBlobByName(blobName);

                if (cb1 is null)
                    throw new InvalidOperationException($"No Chunk for hash {chunkHash.Value}");

                // if we don't need a hydrated chunk, return this one
                if (!requireHydrated)
                    return cb1;

                // if we require a hydrated chunk, and this one is hydrated, return this one
                if (requireHydrated && cb1.Downloadable)
                    return cb1;

                blobName = GetChunkBlobName(RehydrationDirectoryName, chunkHash);
                var cb2 = GetChunkBlobByName(blobName);

                if (cb2 is null || !cb2.Downloadable)
                {
                    // no hydrated chunk exists
                    _logger.LogDebug($"No hydrated chunk found for {chunkHash}");
                    return null;
                }
                else
                    return cb2;
            }

            private string GetChunkBlobName(string folder, HashValue chunkHash) => GetChunkBlobFullName(folder, $"{chunkHash.Value}{ChunkBlobBase.Extension}");
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
                    var bc = _bcc.GetBlobClient(blobName);
                    var cb = ChunkBlobBase.GetChunkBlob(bc);
                    return cb;
                }
                catch (ArgumentException)
                {
                    return null;
                }
            }

            public async Task<bool> ChunkExists(HashValue chunkHash)
            {
                return await _bcc.GetBlobClient(GetChunkBlobName(EncryptedChunkDirectoryName, chunkHash)).ExistsAsync();
            }


            // HYDRATE

            public void Hydrate(ChunkBlobBase blobToHydrate)
            {
                _logger.LogInformation($"Hydrating chunk {blobToHydrate.Name}");

                if (blobToHydrate.AccessTier == AccessTier.Hot ||
                    blobToHydrate.AccessTier == AccessTier.Cool)
                    throw new InvalidOperationException($"Calling Hydrate on a blob that is already hydrated ({blobToHydrate.Name})");

                var hydratedItem = _bcc.GetBlobClient($"{RehydrationDirectoryName}/{blobToHydrate.Name}");

                if (!hydratedItem.Exists())
                {
                    //Start hydration
                    var archiveItem = _bcc.GetBlobClient(blobToHydrate.FullName);
                    hydratedItem.StartCopyFromUri(archiveItem.Uri, new BlobCopyFromUriOptions { AccessTier = AccessTier.Cool, RehydratePriority = RehydratePriority.Standard });

                    _logger.LogInformation($"Hydration started for {blobToHydrate.Name}");
                }
                else
                {
                    // Get hydration status
                    // https://docs.microsoft.com/en-us/azure/storage/blobs/storage-blob-rehydration

                    var status = hydratedItem.GetProperties().Value.ArchiveStatus;
                    if (status == "rehydrate-pending-to-cool" || status == "rehydrate-pending-to-hot")
                        _logger.LogInformation($"Hydration pending for {blobToHydrate.Name}");
                    else if (status == null)
                        _logger.LogInformation($"Hydration done for {blobToHydrate.Name}");
                    else
                        throw new ArgumentException("TODO");
                }
            }


            // DELETE

            public void DeleteHydrateFolder()
            {
                _logger.LogInformation("Deleting temporary hydration folder");

                foreach (var bi in _bcc.GetBlobs(prefix: RehydrationDirectoryName))
                {
                    var bc = _bcc.GetBlobClient(bi.Name);
                    bc.Delete();
                }
            }


            // UPLOAD & DOWNLOAD

            public IEnumerable<ChunkBlobBase> Upload(EncryptedChunkFile[] ecfs, AccessTier tier)
            {
                _blobCopier.Upload(ecfs, tier, EncryptedChunkDirectoryName, false);

                return ecfs.Select(ecf =>
                {
                    if (GetChunkBlobByName(EncryptedChunkDirectoryName, ecf.Name) is var cb)
                        return cb;

                    throw new InvalidOperationException($"Sequence contains no elements - could not create {nameof(ChunkBlobItem)} of uploaded chunk {ecf.Hash}");
                });
            }

            public IEnumerable<EncryptedChunkFile> Download(IEnumerable<ChunkBlobBase> chunkBlobs, DirectoryInfo target, bool flatten)
            {
                chunkBlobs = chunkBlobs.ToArray();

                var downloadedFiles = _blobCopier.Download(chunkBlobs, target, flatten);

                if (chunkBlobs.Count() != downloadedFiles.Count())
                    throw new InvalidOperationException("Amount of downloaded files does not match"); //TODO

                return downloadedFiles.Select(fi => new EncryptedChunkFile(fi)).ToArray();

                //return downloadedFiles.Select(fi2=>
                //{
                //    if (new FileInfo(Path.Combine(target.FullName, fi2.Name)) is var fi)
                //        return new EncryptedChunkFile(null, fi, fi2.Hash);

                //    throw new InvalidOperationException($"Sequence contains no element - {fi.FullName} should have been downloaded but isn't found on disk");
                //});
            }
        }
    }
    
}
