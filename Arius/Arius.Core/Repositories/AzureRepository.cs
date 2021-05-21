using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Arius.Core.Commands;
using Arius.Core.Models;
using Arius.Services;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;

namespace Arius.Repositories
{
    internal partial class AzureRepository
    {
        internal interface IOptions
        {
            public string AccountName { get; }
            public string AccountKey { get; }
            public string Container { get; }
            public string Passphrase { get; }
        }

        public AzureRepository(IOptions options, ILoggerFactory loggerFactory, IBlobCopier blobCopier)
        {
            chunkRepo = new ChunkRepository(options, loggerFactory.CreateLogger<ChunkRepository>(), blobCopier);
            manifestRepo = new ManifestRepository(options, loggerFactory.CreateLogger<ManifestRepository>());
            pointerFileEntryRepo = new PointerFileEntryRepository(options, loggerFactory.CreateLogger<PointerFileEntryRepository>(), loggerFactory);
        }

        // -- CHUNK REPOSITORY
        private readonly ChunkRepository chunkRepo;
        
        public IEnumerable<ChunkBlobBase> GetAllChunkBlobs()
        {
            return chunkRepo.GetAllChunkBlobs();
        }

        public ChunkBlobBase GetChunkBlobByHash(HashValue chunkHash, bool requireHydrated)
        {
            return chunkRepo.GetChunkBlobByHash(chunkHash, requireHydrated);
        }

        public void Hydrate(ChunkBlobBase itemToHydrate)
        {
            chunkRepo.Hydrate(itemToHydrate);
        }

        public void DeleteHydrateFolder()
        {
            chunkRepo.DeleteHydrateFolder();
        }

        public IEnumerable<ChunkBlobBase> Upload(IEnumerable<EncryptedChunkFile> ecfs, AccessTier tier)
        {
            return chunkRepo.Upload(ecfs, tier);
        }

        public IEnumerable<EncryptedChunkFile> Download(IEnumerable<ChunkBlobBase> cbs, DirectoryInfo target, bool flatten)
        {
            return chunkRepo.Download(cbs, target, flatten);
        }


        // -- MANIFEST REPOSITORY
        private readonly ManifestRepository manifestRepo;

        public async Task AddManifestAsync(BinaryFile bf, IChunkFile[] cfs)
        {
            await manifestRepo.AddManifestAsync(bf, cfs);
        }

        public IEnumerable<HashValue> GetAllManifestHashes()
        {
            return manifestRepo.GetAllManifestHashes();
        }

        public async Task<IEnumerable<HashValue>> GetChunkHashesAsync(HashValue manifestHash)
        {
            return await manifestRepo.GetChunkHashesAsync(manifestHash);
        }


        // -- POINTERFILEENTRY REPOSITORY
        private readonly PointerFileEntryRepository pointerFileEntryRepo;

        internal async Task<IEnumerable<DateTime>> GetVersionsAsync()
        {
            return await pointerFileEntryRepo.GetVersionsAsync();
        }

        internal async Task<IEnumerable<PointerFileEntry>> GetCurrentEntries(bool includeDeleted)
        {
            return await pointerFileEntryRepo.GetEntries(DateTime.Now, includeDeleted);
        }

        internal async Task<IEnumerable<PointerFileEntry>> GetEntries(DateTime version, bool includeDeleted)
        {
            return await pointerFileEntryRepo.GetEntries(version, includeDeleted);
        }

        public async Task CreatePointerFileEntryIfNotExistsAsync(PointerFile pointerFile, DateTime version)
        {
            await pointerFileEntryRepo.CreatePointerFileEntryIfNotExistsAsync(pointerFile, version);
        }

        public async Task CreateDeletedPointerFileEntryAsync(PointerFileEntry pfe, DateTime version)
        {
            await pointerFileEntryRepo.CreateDeletedPointerFileEntryAsync(pfe, version);
        }
    }
}
