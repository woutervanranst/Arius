using System;
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

        public IManifestFile CreateManifestFile(IEnumerable<IRemoteEncryptedChunkBlobItem> encryptedChunks, HashValue hash)
        {
            var manifest = new Manifest(encryptedChunks.Select(recb => recb.Name), hash.Value);

            var manifestFile = SaveManifest(manifest);

            _logger.LogDebug($"Created ManifestFile '{manifestFile.Name}'");

            return manifestFile;
        }

        private IManifestFile SaveManifest(Manifest manifest, string manifestFileFullName = null)
        {
            if (manifestFileFullName is null)
            {
                var extension = typeof(LocalManifestFile).GetCustomAttribute<ExtensionAttribute>()!.Extension;
                manifestFileFullName = Path.Combine(_localManifestRepository.FullName, $"{manifest.Hash}{extension}");
            }
            var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true, IgnoreNullValues = true });

            File.WriteAllText(manifestFileFullName, json);

            var manifestFileInfo = new FileInfo(manifestFileFullName);
            var manifestFile = (IManifestFile)_factory.Create(manifestFileInfo, _localManifestRepository);

            _localManifestRepository.Put(manifestFile);

            return manifestFile;
        }

        /// <summary>
        /// Synchronize the manifest files with the local pointers
        /// </summary>
        /// <param name="pointerFiles"></param>
        public void UpdateManifests(IEnumerable<IPointerFile> pointerFiles)
        {
            // Group the pointers by manifest (hash)
            var pointerFilesPerManifestName = pointerFiles
                .GroupBy(pointer => pointer.Hash)
                .ToImmutableDictionary(
                    g => g.Key,
                    g => g.ToList());

            //// TODO QUID BROKEN POINTERFILES > test als point point naar oud/non extisting manifest

            // Update each manifest
            _localManifestRepository.GetAll()
                .AsParallelWithParallelism()
                .ForAll(mf =>
                    UpdateManifest(mf, 
                        pointerFilesPerManifestName.ContainsKey(mf.Hash) ?
                            pointerFilesPerManifestName[mf.Hash] :
                            Array.Empty<IPointerFile>()));  //if the pointer no longer exists and is the only one there will not be an entry for the hash, pass empty-- TODO test this
        }

        public Manifest ReadManifestFile(IManifestFile manifestFile)
        {
            var manifestFileFullName = manifestFile.FullName;
            var json = File.ReadAllText(manifestFileFullName);
            var manifest = JsonSerializer.Deserialize<Manifest>(json);

            return manifest;
        }


        public void UpdateManifest(IManifestFile manifestFile, IEnumerable<IPointerFile> pointerFiles)
        {
            //TODO Assert all hashes equal to the manifest file hash
            var manifest = ReadManifestFile(manifestFile);

            var writeback = UpdateManifest(manifest, pointerFiles);

            if (writeback)
            {
                SaveManifest(manifest, manifestFile.FullName);

                _localManifestRepository.Put(manifestFile);
                _logger.LogInformation($"Manifest '{manifestFile.Hash}' has modified entries, marked for writeback");
            }
        }


        /// <summary>
        /// Synchronize the state of the manifest to the current state of the file system:
        /// Additions, deletions, renames (= add + delete)
        /// </summary>
        private bool UpdateManifest(Manifest manifest, IEnumerable<IPointerFile> pointerFiles)
        {
            var fileSystemEntries = Manifest.GetPointerFileEntries(pointerFiles);
            var lastEntries = manifest.GetLastEntries().ToImmutableArray();

            var addedFiles = fileSystemEntries.Except(lastEntries, _pfeec).ToList();
            var deletedFiles = lastEntries
                .Except(fileSystemEntries, _pfeec)
                .Select(lcfe => lcfe with { IsDeleted = true, CreationTimeUtc = null, LastWriteTimeUtc = null })
                .ToList();

            if (addedFiles.Any())
                addedFiles.ForEach(pfe => _logger.LogInformation($"Added {pfe.RelativeName}"));
            if (deletedFiles.Any())
                deletedFiles.ForEach(pfe => _logger.LogInformation($"Removed {pfe.RelativeName}"));
            //_logger.LogInformation($"Manifest {manifest.Hash} has deleted entries: {deletedFiles.Select(pfe => pfe.RelativeName)}"); 

            manifest.AddEntries(addedFiles);
            manifest.AddEntries(deletedFiles);

            return addedFiles.Any() || deletedFiles.Any();
        }

        private static readonly PointerFileEntryEqualityComparer _pfeec = new PointerFileEntryEqualityComparer();
    }
}