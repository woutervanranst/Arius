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
            _localTemp = config.TempDir.CreateSubdirectory("manifests");

            //Asynchronously download all manifests
            downloadManifestsTask = Task.Run(() =>
            {
                blobcopier.Download("manifests", _localTemp);

                var f = _localTemp.GetFiles("*.7z.manifest")
                    .Select(fi => factory.Create(this, fi));
                //encrypter.Decrypt()

                //    .Select(fi => _factory.Create<ILocalFile>(this, fi)) //TODO FILTER
                //    .ToImmutableArray();

                //return localFiles;

            });
        }

        private readonly Task downloadManifestsTask;
        private readonly DirectoryInfo _localTemp;
        private readonly IBlobCopier _blobcopier;
        private readonly IEncrypter _encrypter;
        private readonly LocalFileFactory _factory;


        public IEnumerable<IManifestFile> GetAll(Expression<Func<IManifestFile, bool>> filter = null)
        {
            downloadManifestsTask.Wait();

            throw new NotImplementedException();
        }

        public IManifestFile GetById(HashValue id)
        {
            downloadManifestsTask.Wait();

            throw new NotImplementedException();
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