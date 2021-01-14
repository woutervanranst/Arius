using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Arius.CommandLine;
using Arius.Models;
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
                _blobCopier = b;

                var o = (IAzureRepositoryOptions)options;

                var connectionString = $"DefaultEndpointsProtocol=https;AccountName={o.AccountName};AccountKey={o.AccountKey};EndpointSuffix=core.windows.net";
                var bsc = new BlobServiceClient(connectionString);
                _bcc = bsc.GetBlobContainerClient(o.Container);
            }

            private readonly IBlobCopier _blobCopier;
            private readonly BlobContainerClient _bcc;
            private const string EncryptedChunkDirectoryName = "chunks";

            public IEnumerable<RemoteEncryptedChunkBlobItem> GetAllChunkBlobItems()
            {
                //var k = _bcc.GetBlobs(prefix: EncryptedChunkDirectoryName + "/").ToList();

                //return _bcc.GetBlobs(prefix: EncryptedChunkDirectoryName + "/")
                //    .Select(bi => new RemoteEncryptedChunkBlobItem(bi));

                return _bcc.GetBlobs(prefix: EncryptedChunkDirectoryName + "/")
                    .Select(bi => new RemoteEncryptedChunkBlobItem(bi));
            }

            private RemoteEncryptedChunkBlobItem GetByName(string name, string folder = EncryptedChunkDirectoryName)
            {
                var bi = _bcc
                    .GetBlobs(prefix: $"{folder}/{name}", traits: BlobTraits.Metadata & BlobTraits.CopyStatus)
                    .Single();

                return new RemoteEncryptedChunkBlobItem(bi);
            }

            public IEnumerable<RemoteEncryptedChunkBlobItem> Upload(IEnumerable<EncryptedChunkFile> ecfs, AccessTier tier)
            {
                _blobCopier.Upload(ecfs, tier, EncryptedChunkDirectoryName, false);

                return ecfs.Select(ecf => GetByName(ecf.Name));
            }
        }
    }
    
}
