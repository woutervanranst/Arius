using System;
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

    internal class LocalManifestFileRepository : IGetRepository<IManifestFile>, IPutRepository<IManifestFile>
    {
        public LocalManifestFileRepository(ICommandExecutorOptions options, 
            IConfiguration config, 
            ILogger<LocalManifestFileRepository> logger,
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
            var modifiedEncryptedManifsts = _modifiedManifestFiles.Values   //TODO BUG: bij modify 1 file worden alle manifests toch geupload
                .AsParallelWithParallelism()
                .Select(mf => (IEncryptedManifestFile)_encrypter.Encrypt(mf, false))
                .ToImmutableArray();

            _blobCopier.Upload(modifiedEncryptedManifsts, $"/{SubDirectoryName}", overwrite: true);
        }

        private const string SubDirectoryName = "manifests";
        //private readonly Task _downloadManifestsTask;
        private readonly DirectoryInfo _localTemp;
        private readonly ILogger<LocalManifestFileRepository> _logger;
        private readonly IBlobCopier _blobCopier;
        private readonly IEncrypter _encrypter;
        private readonly LocalFileFactory _factory;
        private readonly Task<Dictionary<HashValue, IManifestFile>> _manifestFiles;
        private readonly Dictionary<HashValue, IManifestFile> _modifiedManifestFiles = new();

        public string FullName => _localTemp.FullName;


        public IManifestFile GetById(HashValue id)
        {
            return _manifestFiles.Result[id];
        }

        public IEnumerable<IManifestFile> GetAll()
        {
            return _manifestFiles.Result.Values;
        }

        public void Put(IManifestFile manifestFile)
        {
            if (manifestFile.Root != this)
                throw new ArgumentException("DOES NOT BELONG TO ME");

            if (!_manifestFiles.Result.ContainsKey(manifestFile.Hash))
                _manifestFiles.Result.Add(manifestFile.Hash, manifestFile);
            if (!_modifiedManifestFiles.ContainsKey(manifestFile.Hash))
                _modifiedManifestFiles.Add(manifestFile.Hash, manifestFile);
        }

        public void PutAll(IEnumerable<IManifestFile> manifestFiles)
        {
            throw new System.NotImplementedException();
        }
    }
}