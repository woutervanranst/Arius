using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Arius.Extensions;
using Arius.Models;
using Arius.Repositories;
using Microsoft.Extensions.Logging;

namespace Arius.Services
{
    internal class ManifestService
    {
        private readonly ILogger<ManifestService> _logger;
        private readonly LocalManifestFileRepository _localManifestRepository;
        private readonly LocalFileFactory _factory;

        public ManifestService(ILogger<ManifestService> logger,
            LocalManifestFileRepository localManifestRepository,
            LocalFileFactory factory)
        {
            _logger = logger;
            _localManifestRepository = localManifestRepository;
            _factory = factory;
        }

        public IManifestFile CreateManifestFile(IEnumerable<IRemoteEncryptedChunkBlob> encryptedChunks, HashValue hash)
        {
            //_downloadManifestsTask.Wait();

            var manifest = new Manifest(encryptedChunks.Select(recb => recb.Name),
                hash.Value);

            var manifestFileInfo = SaveManifest(manifest);

            _logger.LogDebug($"Created ManifestFile '{manifestFileInfo.Name}'");

            var manifestFile = (IManifestFile)_factory.Create(manifestFileInfo, _localManifestRepository);
            _localManifestRepository.Put(manifestFile);

            return manifestFile;
        }

        private FileInfo SaveManifest(Manifest manifest, string manifestFileFullName = null)
        {
            if (manifestFileFullName is null)
            {
                var extension = typeof(LocalManifestFile).GetCustomAttribute<ExtensionAttribute>()!.Extension;
                manifestFileFullName = Path.Combine(_localManifestRepository.FullName, $"{manifest.Hash}{extension}");
            }
            var json = JsonSerializer.Serialize(manifest,
                new JsonSerializerOptions { WriteIndented = true, IgnoreNullValues = true });

            File.WriteAllText(manifestFileFullName, json);

            return new FileInfo(manifestFileFullName);
        }

        /// <summary>
        /// Synchronize the manifest files with the local pointers
        /// </summary>
        /// <param name="pointers"></param>
        public void UpdateManifests(IEnumerable<IPointerFile> pointers)
        {
            // Group the pointers by manifest (hash)
            var pointersPerManifestName = pointers
                .GroupBy(pointer => pointer.Hash)
                .ToImmutableDictionary(
                    g => g.Key,
                    g => g.ToList());

            //// TODO QUID BROKEN POINTERFILES

            // Update each manifest
            _localManifestRepository.GetAll()
                .AsParallelWithParallelism()
                .ForAll(mf =>
                    UpdateManifest(mf, pointersPerManifestName[mf.Hash]));
        }

        public void UpdateManifest(IManifestFile manifestFile, IEnumerable<IPointerFile> pointers)
        {

            //TODO Assert all hashes equal to the manifest file hash

            var manifestFileFullName = _localManifestRepository.GetById(pointers.First().Hash).FullName;
            var json = File.ReadAllText(manifestFileFullName);
            var manifest = JsonSerializer.Deserialize<Manifest>(json);
            var writeback = manifest!.Update(pointers);

            SaveManifest(manifest, manifestFileFullName);

            if (writeback)
            {
                _localManifestRepository.Put(manifestFile);
                _logger.LogInformation($"Manifest '{manifestFile.Hash}' has modified entries");
            }
        }
    }
}