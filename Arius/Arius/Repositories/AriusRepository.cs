using System;
using System.Collections.Generic;
using System.Collections.Immutable;
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
    internal class AzureRepository
    {
        internal interface IAzureRepositoryOptions : ICommandExecutorOptions
        {
            public string AccountName { get; }
            public string AccountKey { get; }
            public string Container { get; }
        }

        private readonly IBlobCopier _blobCopier;

        public AzureRepository(ICommandExecutorOptions options, IBlobCopier b)
        {
            _blobCopier = b;

            var o = (IAzureRepositoryOptions)options;

            var connectionString = $"DefaultEndpointsProtocol=https;AccountName={o.AccountName};AccountKey={o.AccountKey};EndpointSuffix=core.windows.net";
            var bsc = new BlobServiceClient(connectionString);
            _bcc = bsc.GetBlobContainerClient(o.Container);
        }

        private readonly BlobContainerClient _bcc;

        private const string EncryptedChunkDirectoryName = "chunks";


        public IEnumerable<RemoteEncryptedChunkBlobItem2> GetAllChunkBlobItems()
        {
            //var k = _bcc.GetBlobs(prefix: EncryptedChunkDirectoryName + "/").ToList();

            return _bcc.GetBlobs(prefix: EncryptedChunkDirectoryName + "/")
                .Select(bi => new RemoteEncryptedChunkBlobItem2(bi));
        }

        public RemoteEncryptedChunkBlobItem2 GetByName(string name, string folder = EncryptedChunkDirectoryName)
        {
            var bi = _bcc
                .GetBlobs(prefix: $"{folder}/{name}", traits: BlobTraits.Metadata & BlobTraits.CopyStatus)
                .Single();

            return new RemoteEncryptedChunkBlobItem2(bi);
        }

        public RemoteEncryptedChunkBlobItem2 Upload(EncryptedChunkFile2 ecf, AccessTier tier)
        {
            _blobCopier.Upload(ecf, tier, EncryptedChunkDirectoryName, false);

            return GetByName(ecf.Name);
        }
    }
}
