using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Arius.CommandLine;
using Arius.Models;
using Arius.Services;
using Azure.Storage.Blobs;

namespace Arius.Repositories
{
    internal interface IRemoteChunkRepositoryOptions : ICommandExecutorOptions
    {
        public string AccountName { get; }
        public string AccountKey { get; }
        public string Container { get; }
    }

    internal class RemoteEncryptedChunkRepository : IRepository  //: IGetRepository<IRemoteEncryptedChunkBlob>, IPutRepository<IEncryptedChunkFile>, IDisposable
    {
        public RemoteEncryptedChunkRepository(ICommandExecutorOptions options,
            IConfiguration config,
            IBlobCopier blobcopier,
            RemoteBlobFactory blobFactory,
            LocalFileFactory localFactory)
        {
            _blobcopier = blobcopier;
            _blobFactory = blobFactory;
            _localFactory = localFactory;
            _localTemp = config.TempDir.CreateSubdirectory(SubDirectoryName);

            var o = (IRemoteChunkRepositoryOptions) options;

            var connectionString = $"DefaultEndpointsProtocol=https;AccountName={o.AccountName};AccountKey={o.AccountKey};EndpointSuffix=core.windows.net";
            var bsc = new BlobServiceClient(connectionString);
            _bcc = bsc.GetBlobContainerClient(o.Container);

            if (!_bcc.Exists())  //TODO wanneer moet deze aangemaakt worden?
            {
                //Console.Write($"Creating container {container}... ");
                //bcc = bsc.CreateBlobContainer(container);
                //Console.WriteLine("Done");
            }
        }

        private const string SubDirectoryName = "chunks";
        private readonly DirectoryInfo _localTemp;
        private readonly IBlobCopier _blobcopier;
        private readonly RemoteBlobFactory _blobFactory;
        private readonly LocalFileFactory _localFactory;
        private readonly BlobContainerClient _bcc;
        
        public string FullName => _localTemp.FullName;

        public IRemoteEncryptedChunkBlobItem GetById(string name)
        {
            var bi = _bcc.GetBlobs(prefix: $"{SubDirectoryName}/{name}").Single();
            return _blobFactory.Create<IRemoteEncryptedChunkBlobItem>(bi, this);
        }

        public IEnumerable<IRemoteEncryptedChunkBlobItem> GetAllChunkBlobItems()
        {
            return _bcc.GetBlobs(prefix: SubDirectoryName)
                .Select(bi => _blobFactory.Create<IRemoteEncryptedChunkBlobItem>(bi, this))
                .ToImmutableArray();
        }

        public void PutAll(IEnumerable<IEncryptedChunkFile> entities)
        {
            _blobcopier.Upload(entities, $"/{SubDirectoryName}", overwrite: false);
        }

        public IEnumerable<IEncryptedChunkFile> DownloadAll(IEnumerable<IRemoteEncryptedChunkBlobItem> chunks)
        {
            _blobcopier.Download(chunks, _localTemp);

            return _localTemp.GetFiles("*.*", SearchOption.AllDirectories)
                .Select(fi => (IEncryptedChunkFile)_localFactory.Create(fi, this))
                .ToImmutableArray();
        }
    }
}