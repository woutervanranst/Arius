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

    internal class LocalRootRepository : ILocalRepository
    {
        public LocalRootRepository(ICommandExecutorOptions options, Configuration config, LocalFileFactory factory)
        {
            var root = ((ILocalRootDirectoryOptions)options).Path;
            _root = new DirectoryInfo(root);
            _config = config;
            _factory = factory;
        }

        private readonly DirectoryInfo _root;
        private readonly Configuration _config;
        private readonly LocalFileFactory _factory;

        public string FullName => _root.FullName;

        


        /// <summary>
        /// Return all LocalContentFiles and Pointers in this repository
        /// </summary>
        /// <param name="filter"></param>
        /// <returns></returns>
        public IEnumerable<IArchivable> GetAll()
        {
            var localFiles = _root.GetFiles("*", SearchOption.AllDirectories)
                .Select(fi => _factory.Create<IArchivable>(fi, this)) //TODO FILTER
                .ToImmutableArray();

            return localFiles;
        }

        public IArchivable GetById(HashValue id)
        {
            throw new NotImplementedException();
        }

        public void Put(IArchivable entity)
        {
            throw new NotImplementedException();
        }

        public void PutAll(IEnumerable<IArchivable> entities)
        {
            throw new NotImplementedException();
        }

        //IPointerFile IRepository<IPointerFile, IPointerFile>.GetById(HashValue id)
        //{
        //    throw new NotImplementedException();
        //}

        //IEnumerable<IPointerFile> IRepository<IPointerFile, IPointerFile>.GetAll()
        //{
        //    throw new NotImplementedException();
        //}

        //void IRepository<IPointerFile, IPointerFile>.Put(IPointerFile entity)
        //{
        //    throw new NotImplementedException();
        //}

        //void IRepository<IPointerFile, IPointerFile>.PutAll(IEnumerable<IPointerFile> entities)
        //{
        //    throw new NotImplementedException();
        //}
    }
}
