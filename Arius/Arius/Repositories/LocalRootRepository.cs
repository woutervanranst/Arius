using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using Arius.CommandLine;
using Arius.Extensions;
using Arius.Models;
using Arius.Services;

namespace Arius.Repositories
{
    internal interface ILocalRootDirectoryOptions : ICommandExecutorOptions
    {
        string Path { get; }
    }

    internal class LocalRootRepository : IGetRepository<IArchivable> //, IPutRepository<IManifestFile> // : IGetRepository<ILocalContentFile>, IGetRepository<IPointerFile>

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
        public bool Exists => _root.Exists;
        public bool IsEmpty => !_root.EnumerateFiles().Any();

        public IArchivable GetById(HashValue id)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Return all LocalContentFiles and Pointers in this repository
        /// </summary>
        public IEnumerable<IArchivable> GetAll()
        {
            var localFiles = _root.GetFiles("*", SearchOption.AllDirectories)
                .Select(fi => (IArchivable) _factory.Create(fi, this))
                .ToImmutableArray();

            return localFiles;
        }

        public FileInfo GetPointerFileInfo(Manifest.PointerFileEntry pfe)
        {
            return new FileInfo(GetPointerFileFullName(pfe));
        }

        public FileInfo GetLocalContentFileInfo(Manifest.PointerFileEntry pfe)
        {
            var pffn = GetPointerFileFullName(pfe).TrimEnd((typeof(LocalPointerFile).GetCustomAttribute<ExtensionAttribute>()!.Extension));
            return new FileInfo(pffn);
        }

        private string GetPointerFileFullName(Manifest.PointerFileEntry pfe)
        {
            return Path.Combine(_root.FullName, pfe.RelativeName);
        }

        public void DeleteEmptySubdirectories()
        {
            _root.DeleteEmptySubdirectories();
        }

        //public void PutAll(IEnumerable<IManifestFile> manifestFiles)
        //{
        //    manifestFiles.AsParallelWithParallelism().ForAll(Put);
        //}
    }
}
