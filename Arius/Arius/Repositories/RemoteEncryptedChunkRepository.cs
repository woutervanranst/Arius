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
            Configuration config,
            IBlobCopier blobcopier,
            IEncrypter encrypter,
            RemoteBlobFactory factory)
        {
            _blobcopier = blobcopier;
            _encrypter = encrypter;
            _factory = factory;
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
        private readonly IEncrypter _encrypter;
        private readonly RemoteBlobFactory _factory;
        private readonly BlobContainerClient _bcc;
        
        public string FullName => _localTemp.FullName;

        public IRemoteEncryptedChunkBlobItem GetById(string name)
        {
            var bi = _bcc.GetBlobs(prefix: $"{SubDirectoryName}/{name}").Single();
            return _factory.Create<IRemoteEncryptedChunkBlobItem>(bi, this);
        }

        public IEnumerable<IRemoteEncryptedChunkBlobItem> GetAllChunkBlobItems()
        {
            return _bcc.GetBlobs(prefix: SubDirectoryName)
                .Select(bi => _factory.Create<IRemoteEncryptedChunkBlobItem>(bi, this))
                .ToImmutableArray();
        }

        public void PutAll(IEnumerable<IEncryptedChunkFile> entities)
        {
            _blobcopier.Upload(entities, $"/{SubDirectoryName}", overwrite: false);
        }

        public void GetAll(IEnumerable<IRemoteEncryptedChunkBlobItem> chunks)
        {
            _blobcopier.Download(chunks, _localTemp.Parent); //specifying .Parent as azcopy mirrors the  "/chunks/" structure they are in - otherwise /chunks/chunks"
        }
    }
}