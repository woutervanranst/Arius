using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Metadata;
using System.Text;
using System.Threading.Tasks;
using Arius.CommandLine;
using Azure.Storage.Blobs.Models;

namespace Arius
{
    internal interface IRemoteContainerRepositoryOptions : ICommandExecutorOptions
    {
        public string AccountName { get; init; }
        public string AccountKey { get; init; }
        public string Container { get; init; }
    }

    class RemoteContainerRepository : IRemoteRepository<IEncrypted<IFile>>
    {
        public RemoteContainerRepository(ICommandExecutorOptions options, IUploader<IFile> uploader)
        {
            _uploader = uploader;
        }

        private readonly IUploader<IFile> _uploader;

        IEnumerable<T> IRepository<IEncrypted<IFile>>.Get<T>(Expression<Func<T, bool>> filter)
        {
            throw new NotImplementedException();
        }

        public IEncrypted<IFile> GetByID(object id)
        {
            throw new NotImplementedException();
        }

        public void Add(IEncrypted<IFile> entity)
        {
            throw new NotImplementedException();
        }

        public void Add(IEnumerable<IEncrypted<IFile>> entities)
        {
            _uploader.Upload(entities);
        }

    }




    //    internal class LocalRootDirectory : ILocalRepository<ILocalFile>
    //    {
    //        public LocalRootDirectory(ICommandExecutorOptions options, LocalFileFactory factory)
    //        {
    //            var root = ((ILocalRootDirectoryOptions)options).Path;
    //            _root = new DirectoryInfo(root);
    //            _factory = factory;
    //        }

    //        private readonly DirectoryInfo _root;
    //        private readonly LocalFileFactory _factory;

    //        public IEnumerable<T> Get<T>(Expression<Func<T, bool>> filter = null) where T : class, ILocalFile
    //        {
    //            var attr = typeof(T).GetCustomAttribute<ExtensionAttribute>();
    //            var localFiles = ExtensionAttribute.GetFilesWithExtension(_root, attr).Select(fi => _factory.Create<T>(this, fi));

    //            return localFiles;
    //        }

    //        public DirectoryInfo Root => _root;

    //        public ILocalFile GetByID(object id)
    //        {
    //            throw new NotImplementedException();
    //        }

    //        public void Insert(ILocalFile entity)
    //        {
    //            throw new NotImplementedException();
    //        }

    //        public void Update(ILocalFile entityToUpdate)
    //        {
    //            throw new NotImplementedException();
    //        }


    //    }
    //}

}
