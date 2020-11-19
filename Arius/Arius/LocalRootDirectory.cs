using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Arius.CommandLine;
using Microsoft.Extensions.Configuration;

namespace Arius
{
    internal interface ILocalRootDirectoryOptions : ICommandExecutorOptions
    {
        string Path { get; }
    }

    internal class LocalRootDirectory : ILocalRepository
    {
        public LocalRootDirectory(ICommandExecutorOptions options, Configuration config, LocalFileFactory factory)
        {
            var root = ((ILocalRootDirectoryOptions)options).Path;
            _root = new DirectoryInfo(root);
            _config = config;
            _factory = factory;
        }

        private readonly DirectoryInfo _root;
        private readonly Configuration _config;
        private readonly LocalFileFactory _factory;

        public DirectoryInfo Root => _root;

        
        public void Add(ILocalFile entity)
        {
            throw new NotImplementedException();
        }

        public void Add(IEnumerable<ILocalFile> entities)
        {
            throw new NotImplementedException();
        }

        public ILocalFile GetById(HashValue id)
        {
            throw new NotImplementedException();
        }

        public void Put(ILocalFile entity)
        {
            throw new NotImplementedException();
        }

        public void PutAll(IEnumerable<ILocalFile> entities)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Return all LocalContentFiles and Pointers in this repository
        /// </summary>
        /// <param name="filter"></param>
        /// <returns></returns>
        public IEnumerable<ILocalFile> GetAll(Expression<Func<ILocalFile, bool>> filter = null)
        {
            var localFiles = _root.GetFiles("*", SearchOption.AllDirectories)
                .Select(fi => _factory.Create<ILocalFile>(this, fi)) //TODO FILTER
                .ToImmutableArray();

            return localFiles;
        }
    }
}
