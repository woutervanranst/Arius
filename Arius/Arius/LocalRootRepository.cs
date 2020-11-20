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

    internal class LocalRootRepository : IReadRepository<ILocalContentFile>, IReadRepository<IPointerFile>, IPointerService
    {
        public LocalRootRepository(ICommandExecutorOptions options, Configuration config, LocalFileFactory factory)
        {
            var root = ((ILocalRootDirectoryOptions) options).Path;
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

        ILocalContentFile IReadRepository<ILocalContentFile>.GetById(HashValue id) => (ILocalContentFile)GetById(id);
        IPointerFile IReadRepository<IPointerFile>.GetById(HashValue id) => (IPointerFile)GetById(id);

        IEnumerable<IPointerFile> IReadRepository<IPointerFile>.GetAll() => (IEnumerable<IPointerFile>)GetAll();
        IEnumerable<ILocalContentFile> IReadRepository<ILocalContentFile>.GetAll() => (IEnumerable<ILocalContentFile>)GetAll();
    }
}
