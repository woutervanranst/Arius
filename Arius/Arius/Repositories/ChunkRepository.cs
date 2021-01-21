using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Arius.CommandLine;
using Arius.Extensions;
using Arius.Models;
using Arius.Services;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;

namespace Arius.Repositories
{
    internal partial class AzureRepository
    {
        private class ChunkRepository
        {
            public ChunkRepository(ICommandExecutorOptions options,
                ILogger<ChunkRepository> logger,
                    IBlobCopier b)
            {
                _logger = logger;
                _blobCopier = b;

                var o = (IAzureRepositoryOptions)options;
                var connectionString = $"DefaultEndpointsProtocol=https;AccountName={o.AccountName};AccountKey={o.AccountKey};EndpointSuffix=core.windows.net";
                
                var bsc = new BlobServiceClient(connectionString);
                _bcc = bsc.GetBlobContainerClient(o.Container);
                var r = _bcc.CreateIfNotExists(PublicAccessType.None);
                
                if (r is not null && r.GetRawResponse().Status == 201) // Created
                    _logger.LogInformation($"Created container {o.Container}... ");
            }

            private readonly ILogger<ChunkRepository> _logger;
            private readonly IBlobCopier _blobCopier;
            private readonly BlobContainerClient _bcc;
            private const string EncryptedChunkDirectoryName = "chunks";
            private const string RehydrationDirectoryName = "chunks-rehydrated";


            // GET

            public IEnumerable<RemoteEncryptedChunkBlobItem> GetAllChunkBlobItems()
            {
                return _bcc.GetBlobs(prefix: EncryptedChunkDirectoryName + "/")
                    .Select(bi => new RemoteEncryptedChunkBlobItem(bi));
            }

            /// <summary>
            /// Get a hydrated RemoteEncryptedChunkBlobItem - either from permanent cold storage or from temporary rehydration storage
            /// Throws exception if not found.
            /// </summary>
            /// <param name="chunkHash"></param>
            /// <returns></returns>
            public RemoteEncryptedChunkBlobItem GetHydratedChunkBlobItemByHash(HashValue chunkHash)
            {
                if (GetByName(EncryptedChunkDirectoryName, chunkHash.Value) is var recbi1 
                    && recbi1 is not null 
                    && recbi1.Downloadable)
                    return recbi1;

                if (GetByName(RehydrationDirectoryName, chunkHash.Value) is var recbi2 
                    && recbi2 is not null
                    && recbi2.Downloadable)
                    return recbi2;

                throw new InvalidOperationException($"{nameof(RemoteEncryptedChunkBlobItem)} not found for hash {chunkHash.Value}");
            }

            public RemoteEncryptedChunkBlobItem GetArchiveTierChunkBlobItemByHash(HashValue chunkHash)
            {
                if (GetByName(EncryptedChunkDirectoryName, chunkHash.Value) is var recbi
                    && recbi is not null
                    && recbi.AccessTier == AccessTier.Archive)
                    return recbi;

                throw new InvalidOperationException($"{nameof(RemoteEncryptedChunkBlobItem)} in Archive tier not found for hash {chunkHash.Value}");
            }

            /// <summary>
            /// Get a RemoteEncryptedChunkBlobItem by Name. Return null if it doesn't exist.
            /// </summary>
            /// <returns></returns>
            private RemoteEncryptedChunkBlobItem GetByName(string folder, string name)
            {
                var bi = _bcc
                    .GetBlobs(prefix: $"{folder}/{name}", traits: BlobTraits.Metadata & BlobTraits.CopyStatus)
                    .SingleOrDefault();

                return bi is null 
                    ? null 
                    : new RemoteEncryptedChunkBlobItem(bi);
            }


            // PUT
            public void Hydrate(RemoteEncryptedChunkBlobItem itemToHydrate)
            {
                if (itemToHydrate.AccessTier == AccessTier.Hot ||
                    itemToHydrate.AccessTier == AccessTier.Cool)
                    throw new InvalidOperationException("Already hydrated");

                var hydratedItem = _bcc.GetBlobClient($"{RehydrationDirectoryName}/{itemToHydrate.Name}");

                if (!hydratedItem.Exists())
                {
                    //Start hydration
                    var archiveItem = _bcc.GetBlobClient(itemToHydrate.FullName);
                    hydratedItem.StartCopyFromUri(archiveItem.Uri, new BlobCopyFromUriOptions { AccessTier = AccessTier.Cool, RehydratePriority = RehydratePriority.Standard });

                    //var xx = archiveTierBlobClient.GetProperties().Value;
                    //var xxx = xx.ArchiveStatus == ; //Azure.Storage.Shared. RehydratePendingToCool

                    _logger.LogInformation($"Hydration started for {itemToHydrate.Name}");
                }
                else
                {
                    // Get hydration status
                    // https://docs.microsoft.com/en-us/azure/storage/blobs/storage-blob-rehydration

                    var status = hydratedItem.GetProperties().Value.ArchiveStatus;
                    if (status == "rehydrate-pending-to-cool" || status == "rehydrate-pending-to-hot")
                        _logger.LogInformation($"Hydration pending for {itemToHydrate.Name}");
                    else if (status == null)
                        _logger.LogInformation($"Hydration done for {itemToHydrate.Name}");
                    //return GetByName(itemToHydrate.Name, RehydrationSubdirectoryName);
                    else
                        throw new ArgumentException("TODO");
                }
            }

            // UPLOAD & DOWNLOAD

            public IEnumerable<RemoteEncryptedChunkBlobItem> Upload(IEnumerable<EncryptedChunkFile> ecfs, AccessTier tier)
            {
                ecfs = ecfs.ToArray();

                _blobCopier.Upload(ecfs, tier, EncryptedChunkDirectoryName, false);

                return ecfs.Select(ecf =>
                {
                    if (GetByName(EncryptedChunkDirectoryName, ecf.Name) is var r)
                        return r;

                    throw new InvalidOperationException($"Sequence contains no elements - could not create {nameof(RemoteEncryptedChunkBlobItem)} of uploaded chunk {ecf.Hash}");
                });
            }

            public IEnumerable<EncryptedChunkFile> Download(IEnumerable<RemoteEncryptedChunkBlobItem> recbis, DirectoryInfo target, bool flatten)
            {
                recbis = recbis.ToArray();

                var downloadedFiles = _blobCopier.Download(recbis.Select(recbi => recbi.BlobItem), target, flatten);

                if (recbis.Count() != downloadedFiles.Count())
                    throw new InvalidOperationException("Amount of downloaded files does not match"); //TODO

                return downloadedFiles.Select(fi =>
                {
                    var hash = new HashValue { Value = fi.Name.TrimEnd(EncryptedChunkFile.Extension) };

                    return new EncryptedChunkFile(null, fi, hash);
                });

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
