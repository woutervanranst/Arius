using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Arius.CommandLine;
using Arius.Models;
using Arius.Services;

namespace Arius.Repositories
{
    internal interface ILocalRootDirectoryOptions : ICommandExecutorOptions
    {
        string Path { get; }
    }

    internal class LocalRootRepository : IGetRepository<IArchivable> // : IGetRepository<ILocalContentFile>, IGetRepository<IPointerFile>
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


        /// <summary>
        /// Return all LocalContentFiles and Pointers in this repository
        /// </summary>
        public IEnumerable<IArchivable> GetAll()
        {
            var localFiles = _root.GetFiles("*", SearchOption.AllDirectories)
                .Select(fi => (IArchivable)_factory.Create(fi, this)) //TODO FILTER
                .ToImmutableArray();

            return localFiles;
        }

        public IArchivable GetById(HashValue id)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Create a pointer for a local file with a remote manifest
        /// </summary>
        public IPointerFile CreatePointerFile(AriusRepository repository, ILocalContentFile lcf, IManifestFile manifestFile)
        {
            var pointerFileInfo = lcf.PointerFileInfo;

            if (pointerFileInfo.Exists)
                throw new ArgumentException("The Pointer file already exists"); //TODO i  expect issies here when the binnary is changed?

            File.WriteAllText(pointerFileInfo.FullName, manifestFile.Hash.Value);

            pointerFileInfo.CreationTimeUtc = lcf.CreationTimeUtc;
            pointerFileInfo.LastWriteTimeUtc = lcf.LastWriteTimeUtc;

            return (LocalPointerFile)_factory.Create(pointerFileInfo, repository);
        }
    }

    //[Extension(".pointer.arius", encryptedType: typeof(LocalEncryptedManifestFile))]
    //internal class PointerFile : LocalFile, IPointerFile
    //{
    //    public PointerFile(AriusRepository root, FileInfo fi, Func<ILocalFile, HashValue> hashValueProvider) : base(root, fi, hashValueProvider)
    //    {

    //    }

    //    public string RelativeContentName { get; }
    //    public DateTime CreationTimeUtc { get; set; }
    //    public DateTime LastWriteTimeUtc { get; set; }
    //}
}
