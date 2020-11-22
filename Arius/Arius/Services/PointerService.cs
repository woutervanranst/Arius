using System;
using System.IO;
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
        /// Create a pointer for a local file with a remote manifest
        /// </summary>
        public IPointerFile CreatePointerFile(AriusRepository repository, ILocalContentFile lcf, IManifestFile manifestFile)
        {
            //TODO can be refactored to Put()?


            var pointerFileInfo = lcf.PointerFileInfo;

            if (pointerFileInfo.Exists)
                throw new ArgumentException("The Pointer file already exists"); //TODO i  expect issies here when the binnary is changed?

            File.WriteAllText(pointerFileInfo.FullName, manifestFile.Hash.Value);

            pointerFileInfo.CreationTimeUtc = lcf.CreationTimeUtc;
            pointerFileInfo.LastWriteTimeUtc = lcf.LastWriteTimeUtc;

            return (LocalPointerFile) _factory.Create(pointerFileInfo, repository);
        }



    }
}