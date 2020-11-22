using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Arius.CommandLine;
using Arius.Extensions;
using Arius.Models;
using Arius.Services;
using Microsoft.Extensions.Logging;

namespace Arius.Repositories
{
    internal interface IAriusRepositoryOptions : ICommandExecutorOptions
    {
        public bool KeepLocal { get; init; }
        public int MinSize { get; init; }
        public bool Simulate { get; init; }
    }

    internal class AriusRepository : IPutRepository<IArchivable>
    {
        public AriusRepository(ICommandExecutorOptions options,
            ILogger<AriusRepository> logger,
            IBlobCopier uploader,
            LocalManifestFileRepository manifestRepository,
            LocalRootRepository rootRepository,
            RemoteEncryptedChunkRepository chunkRepository,
            IChunker chunker,
            IEncrypter encrypter,
            ManifestService manifestService
            )
        {
            _options = (IAriusRepositoryOptions)options;
            _logger = logger;
            _blobCopier = uploader;
            _manifestRepository = manifestRepository;
            _rootRepository = rootRepository;
            _remoteChunkRepository = chunkRepository;
            _chunker = chunker;
            _encrypter = encrypter;
            _manifestService = manifestService;
        }

        private readonly IAriusRepositoryOptions _options;
        private readonly ILogger<AriusRepository> _logger;
        private readonly IBlobCopier _blobCopier;
        private readonly LocalManifestFileRepository _manifestRepository;
        private readonly LocalRootRepository _rootRepository;
        private readonly RemoteEncryptedChunkRepository _remoteChunkRepository;
        private readonly IChunker _chunker;
        private readonly IEncrypter _encrypter;
        private readonly ManifestService _manifestService;

        public string FullName => _rootRepository.FullName;

        public void Put(IArchivable entity)
        {
            throw new NotImplementedException();
        }

        public void PutAll(IEnumerable<IArchivable> localFiles)
        {
            localFiles = localFiles.ToImmutableArray();

            ////TODO Simulate
            ////TODO MINSIZE
            ////TODO CHeck if the archive is deduped and password by checking the first amnifest file

            /*
             * 1. Ensure ALL LocalContentFiles (ie. all non-.arius files) are on the remote WITH a Manifest
             */

            //1.1 Ensure all chunks are uploaded
            //var hasherProgress = new ConsoleProgress(localFiles.LongCount());
            var localContentPerHash = localFiles
                .OfType<LocalContentFile>()
                .AsParallel()
                .WithDegreeOfParallelism(1)
                //.Select(lcf =>
                //{
                //    var hash = lcf.Hash;
                //    hasherProgress.AddProgress(1);
                //    return lcf;
                //})
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

            var unencryptedChunksPerLocalContentHash = localContentFilesToUpload
                .AsParallel()
                .WithDegreeOfParallelism(1) //moet dat hier staan om te paralleliseren of bij de GetChunks?
                .ToImmutableDictionary(
                    g => g.Key,
                    g => _chunker.Chunk(g.First()));

            _logger.LogInformation($"After deduplication... {unencryptedChunksPerLocalContentHash.Values.Count()} chunks to upload");

            var remoteChunkHashes = _remoteChunkRepository.GetAll()
                .Select(rcb => rcb.Hash)
                .ToImmutableArray();

            _logger.LogInformation($"Found {remoteChunkHashes.Length} encrypted chunks remote");

            var encryptedChunksToUploadPerHash = unencryptedChunksPerLocalContentHash
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


            //1.2 Create manifests for NEW Content (as they do not exist) - this does not yet include the references to the pointers
            var encryptedChunkPerHash = _remoteChunkRepository.GetAll()
                .ToDictionary(recb => recb.Hash, recb => recb);

            var createdManifestsPerHash = localContentFilesToUpload
                .AsParallelWithParallelism()
                .Select(g => _manifestService.CreateManifestFile(
                    unencryptedChunksPerLocalContentHash[g.First().Hash].Select(cf => encryptedChunkPerHash[cf.Hash]),
                    g.First().Hash))
                //g.Select(lcf => lcf)))
                .ToDictionary(
                    mf => mf.Hash, 
                    mf => mf);

            _logger.LogInformation($"Created {createdManifestsPerHash.Count()} new manifests");


            /*
             * 2. Ensure Pointers exist/are create for ALL LocalContentFiles
             */
            var newPointers = localContentPerHash
                .AsParallel()
                    .WithDegreeOfParallelism(1)
                    .SelectMany(g => g)
                    .Where(lcf => !lcf.PointerFileInfo.Exists)
                    .Select(lcf =>
                    {
                        var manifestFile = createdManifestsPerHash.ContainsKey(lcf.Hash) ?
                            createdManifestsPerHash[lcf.Hash] :
                            _manifestRepository.GetById(lcf.Hash);

                        return _rootRepository.CreatePointerFile(this, lcf, manifestFile);
                    })
                .ToImmutableArray();

            _logger.LogInformation($"Created {newPointers.Count()} new pointers");


            /*
             * 3. Synchronize ALL MANIFESTS with the local file system
             */

            // Get all pointers
            var allPointers = localFiles.OfType<IPointerFile>().Union(newPointers);

            //Update all manifests
            _manifestService.UpdateManifests(allPointers);

            // Upload the CHANGED manifests
            _manifestRepository.UploadModifiedManifests();

            /*
             * 4. Remove LocalContentFiles
             */
            if (!_options.KeepLocal)
                localFiles.OfType<LocalContentFile>().AsParallelWithParallelism().ForAll(fi => fi.Delete());
        }
    }
}
