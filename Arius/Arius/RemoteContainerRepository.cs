using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Metadata;
using System.Text;
using System.Threading.Tasks;
using Arius.CommandLine;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;

namespace Arius
{
    internal interface IRemoteContainerRepositoryOptions : ICommandExecutorOptions
    {
        public string AccountName { get; init; }
        public string AccountKey { get; init; }
        public string Container { get; init; }
    }

    class RemoteContainerRepository : IRemoteRepository
    {
        public RemoteContainerRepository(ICommandExecutorOptions options,
            ILogger<RemoteContainerRepository> logger,
            IBlobCopier uploader,
            IRepository<IManifestFile> manifestRepository,
            IRemoteRepository<IRemoteEncryptedChunkBlob, IEncryptedChunkFile> chunkRepository,
            IChunker chunker,
            IEncrypter encrypter)
        {
            _logger = logger;
            _uploader = uploader;
            _manifestRepository = manifestRepository;
            _remoteChunkRepository = chunkRepository;
            _chunker = chunker;
            _encrypter = encrypter;
        }

        private readonly ILogger<RemoteContainerRepository> _logger;
        private readonly IBlobCopier _uploader;
        private readonly IRepository<IManifestFile> _manifestRepository;
        private readonly IRemoteRepository<IRemoteEncryptedChunkBlob, IEncryptedChunkFile> _remoteChunkRepository;
        private readonly IChunker _chunker;
        private readonly IEncrypter _encrypter;

        public string FullName => throw new NotImplementedException();

        public ILocalFile GetById(HashValue id)
        {
            throw new NotImplementedException();
        }

        public IEnumerable<ILocalFile> GetAll(Expression<Func<ILocalFile, bool>> filter = null)
        {
            throw new NotImplementedException();
        }

        public void Put(ILocalFile entity)
        {
            throw new NotImplementedException();
        }

        public void PutAll(IEnumerable<ILocalFile> localFiles)
        {
            ////TODO Simulate
            ////TODO MINSIZE
            ////TODO CHeck if the archive is deduped and password by checking the first amnifest file

            /*
             * 1. Ensure ALL LocalContentFiles (ie. all non-.arius files) are on the remote WITH a Manifest
             */

            _logger.LogWarning("test");

            //1.1 Ensure all chunks are uploaded
            var localContentPerHash = localFiles
                .OfType<LocalContentFile>()
                .AsParallel()
                .WithDegreeOfParallelism(1)
                .GroupBy(lcf => lcf.Hash)
                .ToImmutableArray();

            _logger.LogInformation($"Found {localContentPerHash.Count()} files");
            _logger.LogDebug(string.Join("; ", localContentPerHash.SelectMany(lcfs => lcfs.Select(lcf => lcf.FullName))));

            var remoteManifestHashes = _manifestRepository.GetAll()
                .Select(f => f.Hash)
                .ToImmutableArray();

            _logger.LogInformation($"Found {remoteManifestHashes.Length} manifests on the remote");

            var localContentFilesToUpload = localContentPerHash
                .Where(g => !remoteManifestHashes.Contains(g.Key)) //TODO to Except
                .ToImmutableArray();

            _logger.LogInformation($"After diff...  {localContentFilesToUpload.Length} files to upload");

            var unencryptedChunksPerHash = localContentFilesToUpload
                .AsParallel()
                .WithDegreeOfParallelism(1) //moet dat hier staan om te paralleliseren of bij de GetChunks?
                .ToImmutableDictionary(
                    g => g.Key,
                    g => _chunker.Chunk(g.First()));

            _logger.LogInformation($"After deduplication... {unencryptedChunksPerHash.Values.Count()} chunks to upload");

            var remoteChunkHashes = _remoteChunkRepository.GetAll()
                .Select(rcb => rcb.Hash)
                .ToImmutableArray();

            _logger.LogInformation($"Found {remoteChunkHashes.Length} encrypted chunks remote");

            var encryptedChunksToUploadPerHash = unencryptedChunksPerHash
                .AsParallel()
                .WithDegreeOfParallelism(1)
                .ToImmutableDictionary(
                    p => p.Key,
                    p => p.Value
                        .Where(uec => !remoteChunkHashes.Contains(uec.Hash)) //TODO met Except
                        .Select(c => (IEncryptedChunkFile)_encrypter.Encrypt(c, c is not ILocalContentFile)).ToImmutableArray()
                ); //TODO naar temp folder

            var encryptedChunksToUpload = encryptedChunksToUploadPerHash.Values
                .SelectMany(eac => eac)
                .ToImmutableArray();

            _logger.LogInformation($"After diff... {encryptedChunksToUpload.Count()} encrypted chunks to upload");

            //Upload Chunks
            _remoteChunkRepository.PutAll(encryptedChunksToUpload);

            //Delete Chunks (niet enkel de uploaded ones maar ook de generated ones)
            foreach (var encryptedChunkFullName in encryptedChunksToUpload
                .Select(uec => uec.FullName)
                .Distinct())
                File.Delete(encryptedChunkFullName);
        }
    }
}
