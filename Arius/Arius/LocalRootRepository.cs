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

        


        ///// <summary>
        ///// Return all LocalContentFiles and Pointers in this repository
        ///// </summary>
        ///// <param name="filter"></param>
        ///// <returns></returns>
        //public IEnumerable<IArchivable> GetAll()
        //{
        //    var localFiles = _root.GetFiles("*", SearchOption.AllDirectories)
        //        .Select(fi => _factory.Create<IArchivable>(fi, this)) //TODO FILTER
        //        .ToImmutableArray();

        //    return localFiles;
        //}


        public TGet GetById<TGet>(HashValue id) where TGet : IItem
        {
            throw new NotImplementedException();
        }

        public IEnumerable<TGet> GetAll<TGet>() where TGet : IItem
        {
            //IPointerFile f = null;
            //var z = (IArchivable)f;
        }

        public void Put(IPointerFile entity) => Put((IArchivable)entity);

        public void PutAll(IEnumerable<IPointerFile> entities) => PutAll((IEnumerable<IArchivable>) entities);  

        


        public void Put(IArchivable entity)
        {
            throw new NotImplementedException();
        }

        public void PutAll(IEnumerable<IArchivable> entities)
        {
            throw new NotImplementedException();
        }
    }
}
