using System;
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

    internal class LocalManifestRepository : IGetRepository<IManifestFile>, IPutRepository<IManifestFile>, IDisposable
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

        public string FullName => _localTemp.FullName;


        public IManifestFile Create(IEnumerable<IRemoteEncryptedChunkBlob> encryptedChunks, IEnumerable<ILocalContentFile> localContentFiles)
        {
            var manifest = new Manifest(localContentFiles, 
                encryptedChunks.Select(recb => recb.Name), 
                localContentFiles.First().Hash.Value);

            var manifestFile = SaveManifest(manifest);

            _logger.LogDebug($"Created ManifestFile '{manifestFile.Name}'");

            return _factory.Create<IManifestFile>(manifestFile, this);
        }

        private FileInfo SaveManifest(Manifest manifest)
        {
            var extension = typeof(LocalManifestFile).GetCustomAttribute<ExtensionAttribute>()!.Extension;
            var manifestFileFullName = Path.Combine(_localTemp.FullName, $"{manifest.Hash}{extension}");
            var json = JsonSerializer.Serialize(manifest,
                new JsonSerializerOptions { WriteIndented = true, IgnoreNullValues = true });
            
            File.WriteAllText(manifestFileFullName, json);

            return new FileInfo(manifestFileFullName);
        }



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

        public void Dispose()
        {
            //Delete the temporary manifest files
            _localTemp.Delete();
        }

        public void Put(IManifestFile entity)
        {
            _downloadManifestsTask.Wait();

            throw new NotImplementedException();
        }

        public void PutAll(IEnumerable<IManifestFile> entities)
        {
            _downloadManifestsTask.Wait();

            entities
                .AsParallelWithParallelism()
                .GroupBy(lf => lf.Hash)
                .ForAll(g =>
                {
                    //Get the Manifest
                    Manifest manifest;
                    string manifestFileFullName;
                    if (_manifestFiles.ContainsKey(g.Key))
                    {
                        manifestFileFullName = _manifestFiles[g.Key].FullName;
                        var jso2n = File.ReadAllText(manifestFileFullName);
                        manifest = JsonSerializer.Deserialize<Manifest>(jso2n);
                    }
                    else
                    {
                        
                    }

                    throw new NotImplementedException();

                    //var writeback = manifest.Update(g.AsEnumerable());

                    
                    //Save
                    

                });
        }

        
    }
}