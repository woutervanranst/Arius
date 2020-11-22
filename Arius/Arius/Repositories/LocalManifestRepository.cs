using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Arius.CommandLine;
using Arius.Extensions;
using Arius.Models;
using Arius.Services;
using Microsoft.Extensions.Logging;

namespace Arius.Repositories
{
    //internal interface IManifestRepositoryOptions : ICommandExecutorOptions
    //{
    //    //public string AccountName { get; init; }
    //    //public string AccountKey { get; init; }
    //    //public string Container { get; init; }
    //}

    internal class LocalManifestRepository : IGetRepository<IManifestFile>
    {
        public LocalManifestRepository(ICommandExecutorOptions options, 
            Configuration config, 
            ILogger<LocalManifestRepository> logger,
            IBlobCopier blobcopier,
            IEncrypter encrypter,
            LocalFileFactory factory)
        {
            _logger = logger;
            _blobCopier = blobcopier;
            _encrypter = encrypter;
            _factory = factory;
            _localTemp = config.TempDir.CreateSubdirectory(SubDirectoryName);
            //_manifestFiles = new Dictionary<HashValue, IManifestFile>();

            //Asynchronously download all manifests
            _manifestFiles = Task.Run(() => DownloadManifests());
        }

        private Dictionary<HashValue, IManifestFile> DownloadManifests()
        {
            _blobCopier.Download(SubDirectoryName, _localTemp);

            var localManifests = _localTemp.GetFiles("*.manifest.7z.arius")
                .Select(fi => (IEncryptedManifestFile)_factory.Create(fi, this))
                .AsParallelWithParallelism()
                .Select(encryptedManifest => (IManifestFile)_encrypter.Decrypt(encryptedManifest, true));

            return localManifests.ToDictionary(mf => mf.Hash, mf => mf);
        }

        public void UploadModifiedManifests()
        {
            var modifiedEncryptedManifsts = _modifiedManifestFiles
                .AsParallelWithParallelism()
                .Select(mf => (IEncryptedManifestFile)_encrypter.Encrypt(mf, false))
                .ToImmutableArray();

            _blobCopier.Upload(modifiedEncryptedManifsts, $"/{SubDirectoryName}", overwrite: true);
        }

        private const string SubDirectoryName = "manifests";
        //private readonly Task _downloadManifestsTask;
        private readonly DirectoryInfo _localTemp;
        private readonly ILogger<LocalManifestRepository> _logger;
        private readonly IBlobCopier _blobCopier;
        private readonly IEncrypter _encrypter;
        private readonly LocalFileFactory _factory;
        private Task<Dictionary<HashValue, IManifestFile>> _manifestFiles;
        private readonly IList<IManifestFile> _modifiedManifestFiles = new List<IManifestFile>();

        public string FullName => _localTemp.FullName;


        public IManifestFile GetById(HashValue id)
        {
            //_downloadManifestsTask.Wait();

            return _manifestFiles.Result[id];
        }

        public IEnumerable<IManifestFile> GetAll()
        {
            //_downloadManifestsTask.Wait();

            return _manifestFiles.Result.Values;
        }

        public IManifestFile CreateManifestFile(IEnumerable<IRemoteEncryptedChunkBlob> encryptedChunks, HashValue hash)
        {
            //_downloadManifestsTask.Wait();

            var manifest = new Manifest(encryptedChunks.Select(recb => recb.Name),
                hash.Value);

            var manifestFileInfo = SaveManifest(manifest);

            _logger.LogDebug($"Created ManifestFile '{manifestFileInfo.Name}'");

            var manifestFile = (IManifestFile)_factory.Create(manifestFileInfo, this);
            _manifestFiles.Result.Add(hash, manifestFile);

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

        public void UpdateManifests(IEnumerable<IPointerFile> pointers)
        {
            // Group the pointers by manifest (hash)
            var ariusPointersPerManifestName = pointers
                .GroupBy(pointer => pointer.Hash)
                .ToImmutableDictionary(
                    g => g.Key,
                    g => g.ToList());

            //// TODO QUID BROKEN POINTERFILES

            // Update each manifest
            _manifestFiles.Result
                .AsParallelWithParallelism()
                .ForAll(mf => 
                    UpdateManifest(mf.Value, ariusPointersPerManifestName[mf.Key]));
        }

        public void UpdateManifest(IManifestFile manifestFile, IEnumerable<IPointerFile> pointers)
        {

            //TODO Assert all hashes equal to the manifest file hash

            var manifestFileFullName = _manifestFiles.Result[pointers.First().Hash].FullName;
            var json = File.ReadAllText(manifestFileFullName);
            var manifest = JsonSerializer.Deserialize<Manifest>(json);
            var writeback = manifest!.Update(pointers);

            SaveManifest(manifest, manifestFileFullName);

            if (writeback)
            {
                _modifiedManifestFiles.Add(manifestFile);
                _logger.LogInformation($"Manifest '{manifestFile.Hash}' has modified entries");
            }
        }
    }
}