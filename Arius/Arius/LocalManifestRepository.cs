﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Arius.CommandLine;
using Microsoft.Extensions.Logging;

namespace Arius
{
    //internal interface IManifestRepositoryOptions : ICommandExecutorOptions
    //{
    //    //public string AccountName { get; init; }
    //    //public string AccountKey { get; init; }
    //    //public string Container { get; init; }
    //}

    internal class LocalManifestRepository : IGetRepository<IManifestFile>/*, IPutRepository<IPointerFile>*/, IDisposable
    {
        public LocalManifestRepository(ICommandExecutorOptions options, 
            Configuration config, 
            ILogger<LocalManifestRepository> logger,
            IBlobCopier blobcopier,
            IEncrypter encrypter,
            LocalFileFactory factory)
        {
            _logger = logger;
            _blobcopier = blobcopier;
            _encrypter = encrypter;
            _factory = factory;
            _localTemp = config.TempDir.CreateSubdirectory(SubDirectoryName);
            //_manifestFiles = new Dictionary<HashValue, IManifestFile>();

            //Asynchronously download all manifests
            _downloadManifestsTask = Task.Run(() => DownloadManifests());
        }

        private void DownloadManifests()
        {
            _blobcopier.Download(SubDirectoryName, _localTemp);

            var localManifests = _localTemp.GetFiles("*.manifest.7z.arius")
                .Select(fi => _factory.Create<IEncryptedManifestFile>(fi, this))
                .AsParallel()
                .WithDegreeOfParallelism(1)
                .Select(encryptedManifest => (IManifestFile)_encrypter.Decrypt(encryptedManifest, true));

            _manifestFiles = localManifests.ToDictionary(mf => mf.Hash, mf => mf);
        }

        private const string SubDirectoryName = "manifests";
        private readonly Task _downloadManifestsTask;
        private readonly DirectoryInfo _localTemp;
        private readonly ILogger<LocalManifestRepository> _logger;
        private readonly IBlobCopier _blobcopier;
        private readonly IEncrypter _encrypter;
        private readonly LocalFileFactory _factory;
        private Dictionary<HashValue, IManifestFile> _manifestFiles;
        private readonly IList<IManifestFile> _modifiedManifestFiles = new List<IManifestFile>();

        public string FullName => _localTemp.FullName;


        public IManifestFile GetById(HashValue id)
        {
            _downloadManifestsTask.Wait();

            //TODO

            throw new NotImplementedException();
        }

        public IEnumerable<IManifestFile> GetAll()
        {
            _downloadManifestsTask.Wait();

            return _manifestFiles.Values;
        }

        public IManifestFile CreateManifestFile(IEnumerable<IRemoteEncryptedChunkBlob> encryptedChunks, HashValue hash)
        {
            var manifest = new Manifest(encryptedChunks.Select(recb => recb.Name),
                hash.Value);

            var manifestFileInfo = SaveManifest(manifest);

            _logger.LogDebug($"Created ManifestFile '{manifestFileInfo.Name}'");

            var manifestFile = _factory.Create<IManifestFile>(manifestFileInfo, this);
            _manifestFiles.Add(hash, manifestFile);

            return manifestFile;

        }

        private FileInfo SaveManifest(Manifest manifest, string manifestFileFullName = null)
        {
            if (manifestFileFullName is null)
            { 
                var extension = typeof(LocalManifestFile).GetCustomAttribute<ExtensionAttribute>()!.Extension;
                manifestFileFullName = Path.Combine(_localTemp.FullName, $"{manifest.Hash}{extension}");
            }
            var json = JsonSerializer.Serialize(manifest,
                new JsonSerializerOptions { WriteIndented = true, IgnoreNullValues = true });

            File.WriteAllText(manifestFileFullName, json);

            return new FileInfo(manifestFileFullName);
        }

        public void UpdateManifest(IManifestFile manifestFile, IEnumerable<IPointerFile> entities)
        {
            _downloadManifestsTask.Wait();

            //TODO Assert all hashes equal to the manifest file hash

            var manifestFileFullName = _manifestFiles[entities.First().Hash].FullName;
            var json = File.ReadAllText(manifestFileFullName);
            var manifest = JsonSerializer.Deserialize<Manifest>(json);
            var writeback = manifest!.Update(entities);

            SaveManifest(manifest, manifestFileFullName);

            if (writeback)
                _modifiedManifestFiles.Add(manifestFile);
        }
        public void Dispose()
        {
            //Delete the temporary manifest files
            _localTemp.Delete();
        }
        
    }
}