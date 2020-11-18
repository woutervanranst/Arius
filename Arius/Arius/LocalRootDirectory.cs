using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Arius.CommandLine;

namespace Arius
{
    internal interface ILocalRootDirectoryOptions : ICommandExecutorOptions
    {
        string Path { get; }
    }

    internal class LocalRootDirectory : ILocalRepository<ILocalFile>
    {
        public LocalRootDirectory(ICommandExecutorOptions options, LocalFileFactory factory)
        {
            var root = ((ILocalRootDirectoryOptions)options).Path;
            _root = new DirectoryInfo(root);
            _factory = factory;
        }

        private readonly DirectoryInfo _root;
        private readonly LocalFileFactory _factory;

        public DirectoryInfo Root => _root;

        public ILocalFile GetByID(object id)
        {
            throw new NotImplementedException();
        }

        public IEnumerable<T> Get<T>(Expression<Func<T, bool>> filter = null) where T : class, ILocalFile
        {
            var attr = typeof(T).GetCustomAttribute<ExtensionAttribute>();
            var localFiles = ExtensionAttribute.GetFilesWithExtension(_root, attr).Select(fi => _factory.Create<T>(this, fi));

            return localFiles;
        }

        public void Add(ILocalFile entity)
        {
            throw new NotImplementedException();
        }

        public void Add(IEnumerable<ILocalFile> entities)
        {
            throw new NotImplementedException();
        }
    }
}
