using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Arius.CommandLine;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace Arius
{
    internal interface IRemoteChunkRepositoryOptions : ICommandExecutorOptions
    {
        public string AccountName { get; }
        public string AccountKey { get; }
        public string Container { get; }
    }

    internal class RemoteEncryptedChunkRepository : IRepository<IRemoteEncryptedChunkBlob, IEncryptedChunkFile>, IDisposable
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

        public IRemoteEncryptedChunkBlob GetById(HashValue id)
        {
            throw new NotImplementedException();
        }

        public IEnumerable<IRemoteEncryptedChunkBlob> GetAll()
        {
            return _bcc.GetBlobs(prefix: SubDirectoryName)
                .Select(bi => _factory.Create<IRemoteEncryptedChunkBlob>(bi, this))
                .ToImmutableArray();
        }

        public void Put(IEncryptedChunkFile entity)
        {
            throw new NotImplementedException();
        }

        public void PutAll(IEnumerable<IEncryptedChunkFile> entities)
        {
            _blobcopier.Upload(entities, $"/{SubDirectoryName}");
        }


        public void Dispose()
        {
            throw new NotImplementedException();
        }
    }
}