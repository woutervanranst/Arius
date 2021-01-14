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
    internal partial class AzureRepository
    {
        internal interface IAzureRepositoryOptions : ICommandExecutorOptions
        {
            public string AccountName { get; }
            public string AccountKey { get; }
            public string Container { get; }
        }

        public AzureRepository(ICommandExecutorOptions options, 
            ILoggerFactory loggerFactory,
            IBlobCopier blobCopier)
        {
            _manifestRepository = new ManifestRepository(options, loggerFactory.CreateLogger<ManifestRepository>());
            _chunkRepository = new ChunkRepository(options, loggerFactory.CreateLogger<ChunkRepository>(), blobCopier);
        }

        // -- CHUNK REPOSITORY
        private readonly ChunkRepository _chunkRepository;
        
        public IEnumerable<RemoteEncryptedChunkBlobItem> GetAllChunkBlobItems()
        {
            return _chunkRepository.GetAllChunkBlobItems();
        }

        public IEnumerable<RemoteEncryptedChunkBlobItem> Upload(IEnumerable<EncryptedChunkFile> ecfs, AccessTier tier)
        {
            return _chunkRepository.Upload(ecfs, tier);
        }


        // -- MANIFEST REPOSITORY
        private readonly ManifestRepository _manifestRepository;

        public ManifestEntry AddManifest(BinaryFile f)
        {
            return _manifestRepository.AddManifest(f);
        }

        public void UpdateManifest(DirectoryInfo root, PointerFile pointerFile, DateTime version)
        {
            _manifestRepository.UpdateManifest(root, pointerFile, version);
        }
        public IEnumerable<ManifestEntry> GetAllEntries()
        {
            return _manifestRepository.GetAllEntries();
        }

        public void SetDeleted(ManifestEntry me, PointerFileEntry pfe, DateTime version)
        {
            _manifestRepository.SetDeleted(me, pfe, version);
        }
        public List<ManifestEntry> GetAllManifestEntriesWithChunksAndPointerFileEntries()
        {
            return _manifestRepository.GetAllManifestEntriesWithChunksAndPointerFileEntries();
        }
    }
}
