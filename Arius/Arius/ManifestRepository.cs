using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Arius.CommandLine;

namespace Arius
{
    internal interface IManifestRepositoryOptions : ICommandExecutorOptions
    {
        //public string AccountName { get; init; }
        //public string AccountKey { get; init; }
        //public string Container { get; init; }
    }

    internal class ManifestRepository : IRepository<IManifestFile, IKaka>, IDisposable
    {
        public ManifestRepository(ICommandExecutorOptions options, 
            Configuration config, 
            IBlobCopier blobcopier,
            IEncrypter encrypter,
            LocalFileFactory factory)
        {
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
        private readonly IBlobCopier _blobcopier;
        private readonly IEncrypter _encrypter;
        private readonly LocalFileFactory _factory;
        private Dictionary<HashValue, IManifestFile> _manifestFiles;

        public string FullName => _localTemp.FullName;


        public IManifestFile GetById(HashValue id)
        {
            _downloadManifestsTask.Wait();

            throw new NotImplementedException();
        }

        public IEnumerable<IManifestFile> GetAll(Expression<Func<IManifestFile, bool>> filter = null)
        {
            _downloadManifestsTask.Wait();

            return _manifestFiles.Values; //TODO FILTER
        }

        public void Dispose()
        {
            //Delete the temporary manifest files
            _localTemp.Delete();
        }

        public void Put(IKaka entity)
        {
            _downloadManifestsTask.Wait();

            throw new NotImplementedException();
        }

        public void PutAll(IEnumerable<IKaka> entities)
        {
            _downloadManifestsTask.Wait();

            entities
                .AsParallel()
                .WithDegreeOfParallelism(1)
                .GroupBy(lf => lf.Hash)
                .ForAll(g =>
                {
                    //Get the Manifest
                    AriusManifest manifest;
                    string manifestFileFullName;
                    if (_manifestFiles.ContainsKey(g.Key))
                    {
                        manifestFileFullName = _manifestFiles[g.Key].FullName;
                        var jso2n = File.ReadAllText(manifestFileFullName);
                        manifest = JsonSerializer.Deserialize<AriusManifest>(jso2n);
                    }
                    else
                    {
                        manifest = new AriusManifest(new[] {g.Key.Value}, g.Key.Value); //TODO quid encryptedChunks van Dedup
                        manifestFileFullName = g.Key.Value;
                    }


                    var writeback = manifest.Update(g.AsEnumerable());

                    
                    //Save
                    var json = JsonSerializer.Serialize(this,
                        new JsonSerializerOptions { WriteIndented = true, IgnoreNullValues = true });
                    File.WriteAllText(manifestFileFullName, json);

                });
        }
    }
}