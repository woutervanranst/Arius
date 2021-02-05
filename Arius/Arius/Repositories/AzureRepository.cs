﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Arius.CommandLine;
using Arius.Models;
using Arius.Services;
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
            public string Passphrase { get; }
        }

        public AzureRepository(ICommandExecutorOptions options, ILoggerFactory loggerFactory, IBlobCopier blobCopier)
        {
            _chunkRepository = new ChunkRepository(options, loggerFactory.CreateLogger<ChunkRepository>(), blobCopier);
            _manifestRepository = new ManifestRepository(options, loggerFactory.CreateLogger<ManifestRepository>());
            _pointerFileEntryRepository = new PointerFileEntryRepository(options, loggerFactory.CreateLogger<PointerFileEntryRepository>(), loggerFactory);
        }

        // -- CHUNK REPOSITORY
        private readonly ChunkRepository _chunkRepository;
        
        public IEnumerable<RemoteEncryptedChunkBlobItem> GetAllChunkBlobItems()
        {
            return _chunkRepository.GetAllChunkBlobItems();
        }

        public RemoteEncryptedChunkBlobItem GetHydratedChunkBlobItemByHash(HashValue chunkHash)
        {
            return _chunkRepository.GetHydratedChunkBlobItemByHash(chunkHash);
        }

        public RemoteEncryptedChunkBlobItem GetArchiveTierChunkBlobItemByHash(HashValue chunkHash)
        {
            return _chunkRepository.GetArchiveTierChunkBlobItemByHash(chunkHash);
        }

        public void Hydrate(RemoteEncryptedChunkBlobItem itemToHydrate)
        {
            _chunkRepository.Hydrate(itemToHydrate);
        }

        public void DeleteHydrateFolder()
        {
            _chunkRepository.DeleteHydrateFolder();
        }

        public IEnumerable<RemoteEncryptedChunkBlobItem> Upload(IEnumerable<EncryptedChunkFile> ecfs, AccessTier tier)
        {
            return _chunkRepository.Upload(ecfs, tier);
        }

        public IEnumerable<EncryptedChunkFile> Download(IEnumerable<RemoteEncryptedChunkBlobItem> recbis, DirectoryInfo target, bool flatten)
        {
            return _chunkRepository.Download(recbis, target, flatten);
        }


        // -- MANIFEST REPOSITORY
        private readonly ManifestRepository _manifestRepository;

        public async Task AddManifestAsync(BinaryFile bf, IChunkFile[] cfs)
        {
            await _manifestRepository.AddManifestAsync(bf, cfs);
        }

        public IEnumerable<HashValue> GetAllManifestHashes()
        {
            return _manifestRepository.GetAllManifestHashes();
        }

        public async Task<IEnumerable<HashValue>> GetChunkHashesAsync(HashValue manifestHash)
        {
            return await _manifestRepository.GetChunkHashesAsync(manifestHash);
        }


        // -- POINTERFILEENTRY REPOSITORY
        private readonly PointerFileEntryRepository _pointerFileEntryRepository;

        internal IEnumerable<PointerFileEntry> GetCurrentEntries(bool includeLastDeleted)
        {
            return _pointerFileEntryRepository.GetCurrentEntriesAsync(includeLastDeleted).Result;
        }

        internal async Task<IEnumerable<PointerFileEntry>> GetCurrentEntriesAsync(bool includeLastDeleted)
        {
            return await _pointerFileEntryRepository.GetCurrentEntriesAsync(includeLastDeleted);
        }

        internal async Task<IEnumerable<PointerFileEntry>> GetCurrentEntriesAsync(bool includeLastDeleted, HashValue manifestHash)
        {
            return (await GetCurrentEntriesAsync(includeLastDeleted)).Where(pfe => pfe.ManifestHash.Equals(manifestHash));
        }


        public async Task CreatePointerFileEntryIfNotExistsAsync(PointerFile pointerFile, DateTime version)
        {
            await _pointerFileEntryRepository.CreatePointerFileEntryIfNotExistsAsync(pointerFile, version);
        }

        public async Task CreatePointerFileEntryIfNotExistsAsync(PointerFileEntry pfe, DateTime version, bool isDeleted = false)
        {
            await _pointerFileEntryRepository.CreatePointerFileEntryIfNotExistsAsync(pfe, version, isDeleted);
        }

        //public List<ManifestEntry> GetAllManifestEntriesWithChunksAndPointerFileEntries()
        //{
        //    //var x = new ExpandoObject();

        //    //foreach (var yy in _pointerFileEntryRepository.GetAllEntries())
        //    //{

        //    //}

        //    return null; // TODO


        //    //public List<ManifestEntry2> GetAllManifestEntriesWithChunksAndPointerFileEntries()
        //    //{
        //    //    throw new NotImplementedException();

        //    //    //using var db = new ManifestStore();
        //    //    //return db.Manifests
        //    //    //    .Include(a => a.Chunks)
        //    //    //    .Include(a => a.Entries)
        //    //    //    .ToList();
        //    //}

        //    //return _manifestRepository.GetAllManifestEntriesWithChunksAndPointerFileEntries();
        //}
    }
}
