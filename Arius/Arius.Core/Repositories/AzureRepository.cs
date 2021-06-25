using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Arius.Core.Commands;
using Arius.Core.Models;
using Arius.Core.Services;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using static Arius.Core.Facade.Facade;

namespace Arius.Core.Repositories
{
    internal partial class AzureRepository
    {
        internal interface IOptions
        {
            string AccountName { get; }
            string AccountKey { get; }
            string Container { get; }
            string Passphrase { get; }
        }

        //public AzureRepository(IOptions options, ILoggerFactory loggerFactory, IBlobCopier blobCopier)
        //{
        //    chunkRepo = new ChunkRepository(options, loggerFactory.CreateLogger<ChunkRepository>(), blobCopier);
        //    manifestRepo = new ManifestRepository(options, loggerFactory.CreateLogger<ManifestRepository>());
        //    pointerFileEntryRepo = new PointerFileEntryRepository(options, loggerFactory.CreateLogger<PointerFileEntryRepository>(), loggerFactory);
        //}

        public AzureRepository(ChunkRepository cr, ManifestRepository mrm, PointerFileEntryRepository pfer)
        {
            chunkRepo = cr;
            manifestRepo = mrm;
            pointerFileEntryRepo = pfer;
        }

        // -- CHUNK REPOSITORY
        private readonly ChunkRepository chunkRepo;
        
        public ChunkBlobBase[] GetAllChunkBlobs()
        {
            return chunkRepo.GetAllChunkBlobs();
        }

        public ChunkBlobBase GetChunkBlobByHash(HashValue chunkHash, bool requireHydrated)
        {
            return chunkRepo.GetChunkBlobByHash(chunkHash, requireHydrated);
        }

        internal ChunkBlobBase GetChunkBlobByName(string folder, string name)
        {
            return chunkRepo.GetChunkBlobByName(folder, name);
        }

        public async Task<bool> ChunkExists(HashValue chunkHash)
        {
            return await chunkRepo.ChunkExists(chunkHash);
        }

        public void Hydrate(ChunkBlobBase itemToHydrate)
        {
            chunkRepo.Hydrate(itemToHydrate);
        }

        public void DeleteHydrateFolder()
        {
            chunkRepo.DeleteHydrateFolder();
        }

        public IEnumerable<ChunkBlobBase> Upload(EncryptedChunkFile[] ecfs, AccessTier tier)
        {
            // TODO is the return actually needed? Remove
            return chunkRepo.Upload(ecfs, tier);
        }

        public IEnumerable<EncryptedChunkFile> Download(IEnumerable<ChunkBlobBase> cbs, DirectoryInfo target, bool flatten)
        {
            // TODO is the return actually needed? Remove
            return chunkRepo.Download(cbs, target, flatten);
        }


        // -- MANIFEST REPOSITORY
        private readonly ManifestRepository manifestRepo;

        public async Task AddManifestAsync(BinaryFile binaryFile, IChunkFile[] chunkFiles)
        {
            await manifestRepo.AddManifestAsync(binaryFile, chunkFiles);
        }
        public async Task AddManifestAsync(HashValue manifestHash, HashValue[] chunkHashes)
        {
            await manifestRepo.AddManifestAsync(manifestHash, chunkHashes);
        }


        internal ManifestBlob[] GetAllManifestBlobs()
        {
            return manifestRepo.GetAllManifestBlobs();
        }

        public HashValue[] GetAllManifestHashes()
        {
            return manifestRepo.GetAllManifestHashes();
        }

        public async Task<HashValue[]> GetChunkHashesForManifestAsync(HashValue manifestHash)
        {
            return await manifestRepo.GetChunkHashesForManifestAsync(manifestHash);
        }

        public async Task<bool> ManifestExistsAsync(HashValue manifestHash)
        {
            return await manifestRepo.ManifestExistsAsync(manifestHash);
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

        public async Task<PointerFileEntryRepository.CreatePointerFileEntryResult> CreatePointerFileEntryIfNotExistsAsync(PointerFile pointerFile, DateTime version)
        {
            return await pointerFileEntryRepo.CreatePointerFileEntryIfNotExistsAsync(pointerFile, version);
        }

        public async Task CreateDeletedPointerFileEntryAsync(PointerFileEntry pfe, DateTime version)
        {
            await pointerFileEntryRepo.CreateDeletedPointerFileEntryAsync(pfe, version);
        }
    }
}
