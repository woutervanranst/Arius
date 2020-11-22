using System;
using System.IO;
using System.Reflection;
using Arius.Extensions;
using Arius.Models;
using Arius.Repositories;

namespace Arius.Services
{
    internal class PointerService
    {
        private readonly LocalFileFactory _factory;

        public PointerService(LocalFileFactory factory)
        {
            _factory = factory;
        }
        /// <summary>
        /// Create a pointer from a LocalContentFile
        /// </summary>
        public IPointerFile CreatePointerFile(LocalRootRepository root, ILocalContentFile lcf, IManifestFile manifestFile)
        {
            return CreatePointerFile(root, lcf.PointerFileInfo, manifestFile, lcf.CreationTimeUtc, lcf.LastWriteTimeUtc);
        }

        /// <summary>
        /// Create a pointer from a PointerFileEntry
        /// </summary>
        public IPointerFile CreatePointerFile(LocalRootRepository root, Manifest.PointerFileEntry pfe, IManifestFile manifestFile)
        {
            return CreatePointerFile(root, root.GetPointerFileInfo(pfe), manifestFile, pfe.CreationTimeUtc!.Value, pfe.LastWriteTimeUtc!.Value);
        }

        private IPointerFile CreatePointerFile(LocalRootRepository root, FileInfo pointerFileInfo, IManifestFile manifestFile, DateTime creationTimeUtc, DateTime lastWriteTimeUtc)
        {
            var encryptedManifestFileName = manifestFile.GetType().GetCustomAttribute<ExtensionAttribute>()!.GetEncryptedFileInfo(manifestFile);

            if (pointerFileInfo.Exists)
                throw new ArgumentException("The Pointer file already exists"); //TODO i  expect issies here when the binnary is changed?

            if (!pointerFileInfo.Directory!.Exists)
                pointerFileInfo.Directory.Create();

            File.WriteAllText(pointerFileInfo.FullName, encryptedManifestFileName.Name);

            pointerFileInfo.CreationTimeUtc = creationTimeUtc;
            pointerFileInfo.LastWriteTimeUtc = lastWriteTimeUtc;

            return (LocalPointerFile)_factory.Create(pointerFileInfo, root);
        }
    }
}