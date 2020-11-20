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
    //internal interface IRemoteContainerRepositoryOptions : ICommandExecutorOptions
    //{
    //    public string AccountName { get; init; }
    //    public string AccountKey { get; init; }
    //    public string Container { get; init; }
    //}

    internal class AriusRepository : IPutRepository<IArchivable>
    {
        public AriusRepository(ICommandExecutorOptions options,
            ILogger<AriusRepository> logger,
            IBlobCopier uploader,
            LocalManifestRepository manifestRepository,
            LocalRootRepository rootRepository,
            RemoteEncryptedChunkRepository chunkRepository,
            IChunker chunker,
            IEncrypter encrypter
            )
        {
            _logger = logger;
            _uploader = uploader;
            _manifestRepository = manifestRepository;
            _rootRepository = rootRepository;
            _remoteChunkRepository = chunkRepository;
            _chunker = chunker;
            _encrypter = encrypter;
        }

        private readonly ILogger<AriusRepository> _logger;
        private readonly IBlobCopier _uploader;
        private readonly LocalManifestRepository _manifestRepository;
        private readonly LocalRootRepository _rootRepository;
        private readonly RemoteEncryptedChunkRepository _remoteChunkRepository;
        private readonly IChunker _chunker;
        private readonly IEncrypter _encrypter;

        public string FullName => throw new NotImplementedException();

        public void Put(IArchivable entity)
        {
            throw new NotImplementedException();
        }

        public void PutAll(IEnumerable<IArchivable> localFiles)
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
                .Select(g => _manifestRepository.CreateManifestFile(
                    unencryptedChunksPerLocalContentHash[g.First().Hash].Select(cf => encryptedChunkPerHash[cf.Hash]),
                    g.First().Hash))
                //g.Select(lcf => lcf)))
                .ToDictionary(
                    mf => mf.Hash, 
                    mf => mf);


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


            /*
             * 3. Synchronize ALL MANIFESTS with the local file system
             */

            var allPointers = localFiles.OfType<IPointerFile>().Union(newPointers);

            var ariusPointersPerManifestName = allPointers
                .GroupBy(pointer => pointer.Hash)
                .ToImmutableDictionary(
                    g => g.Key,
                    g => g.ToList());

            //// TODO QUID BROKEN POINTERFILES

            _manifestRepository.GetAll()
                .AsParallelWithParallelism()
                .ForAll(mf => _manifestRepository
                    .UpdateManifest(mf, ariusPointersPerManifestName[mf.Hash]));

            ////TODO met AZCOPY
            //archive.GetRemoteEncryptedAriusManifests()
            //    .AsParallel()
            //    //.WithDegreeOfParallelism(1)
            //    .ForAll(a =>
            //    {
            //        a.Update(ariusPointersPerManifestName[a.Name], passphrase);
            //    });

            ///*
            // * 4. Remove LocalContentFiles
            // */
            //if (!keepLocal)
            //    root.GetNonAriusFiles().AsParallel().ForAll(fi => fi.Delete());






            ///*
            // * 3. Synchronize ALL MANIFESTS with the local file system
            // */

            //// 3.1 Synchronize all 

            //var allPointers = _rootRepository.GetAll().OfType<IPointerFile>();
            //_manifestRepository.PutAll(allPointers);


            ////    var ariusPointersPerManifestName = root.GetAriusPointerFiles()
            ////        .GroupBy(apf => apf.EncryptedManifestName)
            ////        .ToImmutableDictionary(
            ////            g => g.Key,
            ////            g => g.ToList());

            ////// TODO QUID BROKEN POINTERFILES

            //////TODO met AZCOPY
            ////archive.GetRemoteEncryptedAriusManifests()
            ////        .AsParallel()
            ////            //.WithDegreeOfParallelism(1)
            ////        .ForAll(a =>
            ////        {
            ////    a.Update(ariusPointersPerManifestName[a.Name], passphrase);
            ////});



            ////    /*
            ////     * 4. Remove LocalContentFiles
            ////     */
            ////    if (!keepLocal)
            ////        root.GetNonAriusFiles().AsParallel().ForAll(fi => fi.Delete());

        }
    }
}
