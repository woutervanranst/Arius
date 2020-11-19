using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
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

    internal class ManifestRepository : IRepository<IManifestFile>, IDisposable
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
            _manifestFiles = new List<IManifestFile>();

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

            _manifestFiles.AddRange(localManifests.ToList());
        }

        private const string SubDirectoryName = "manifests";
        private readonly Task _downloadManifestsTask;
        private readonly DirectoryInfo _localTemp;
        private readonly IBlobCopier _blobcopier;
        private readonly IEncrypter _encrypter;
        private readonly LocalFileFactory _factory;
        private readonly List<IManifestFile> _manifestFiles;

        public string FullName => _localTemp.FullName;


        public IManifestFile GetById(HashValue id)
        {
            _downloadManifestsTask.Wait();

            throw new NotImplementedException();
        }

        public IEnumerable<IManifestFile> GetAll(Expression<Func<IManifestFile, bool>> filter = null)
        {
            _downloadManifestsTask.Wait();

            return _manifestFiles; //TODO FILTER
        }

        public void Dispose()
        {
            //Delete the temporary manifest files
            _localTemp.Delete();
        }

        public void Put(IManifestFile entity)
        {
            throw new NotImplementedException();
        }

        public void PutAll(IEnumerable<IManifestFile> entities)
        {
            throw new NotImplementedException();
        }

    }
}