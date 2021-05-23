using System;
using System.IO;
using Arius.Core.Models;
using Arius.Core.Repositories;
using Microsoft.Extensions.Logging;

namespace Arius.Core.Services
{
    internal class PointerService
    {
        private readonly ILogger<PointerService> _logger;

        public PointerService(ILogger<PointerService> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Create a pointer from a BinaryFile
        /// </summary>
        public PointerFile CreatePointerFileIfNotExists(BinaryFile f)
        {
            var pointerFileInfo = f.PointerFileInfo;

            return CreatePointerFileIfNotExists(
                f.Root,
                pointerFileInfo,
                f.Hash,
                File.GetCreationTimeUtc(f.FullName),
                File.GetLastWriteTimeUtc(f.FullName));
        }

        /// <summary>
        /// Create a pointer from a PointerFileEntry
        /// </summary>
        public PointerFile CreatePointerFileIfNotExists(DirectoryInfo root, AzureRepository.PointerFileEntry pfe)
        {
            var pointerFileInfo = new FileInfo(Path.Combine(root.FullName, pfe.RelativeName));

            //if (pointerFileInfo.Exists)
            //    throw new ArgumentException("The Pointer file already exists"); //TODO i  expect issies here when the binnary is changed?

            return CreatePointerFileIfNotExists(root,
                pointerFileInfo,
                pfe.ManifestHash,
                pfe.CreationTimeUtc!.Value,
                pfe.LastWriteTimeUtc!.Value);
        }

        private PointerFile CreatePointerFileIfNotExists(DirectoryInfo root, FileInfo pointerFileInfo, HashValue manifestHash, DateTime creationTimeUtc, DateTime lastWriteTimeUtc)
        {
            if (!pointerFileInfo.Exists)
            {
                if (!pointerFileInfo.Directory!.Exists)
                    pointerFileInfo.Directory.Create();

                File.WriteAllText(pointerFileInfo.FullName, manifestHash.Value);

                //FileInfo does not work on Linux according to https://stackoverflow.com/a/17126045/1582323
                //pointerFileInfo.CreationTimeUtc = creationTimeUtc;
                //pointerFileInfo.LastWriteTimeUtc = lastWriteTimeUtc;

                File.SetCreationTimeUtc(pointerFileInfo.FullName, creationTimeUtc);
                File.SetLastWriteTimeUtc(pointerFileInfo.FullName, lastWriteTimeUtc);

                _logger.LogInformation($"Created PointerFile '{Path.GetRelativePath(root.FullName, pointerFileInfo.FullName)}'");
            }

            var pf = new PointerFile(root, pointerFileInfo);

            //Check whether the contents of the PointerFile are correct / is it a valid POinterFile / does the hash it refer to match the manifestHash (eg. not in the case of 0 bytes or ...)
            if (!pf.Hash.Equals(manifestHash))
            {
                //throw new ApplicationException($"The PointerFile {pf.RelativeName} is out of sync. Delete the file and restart the operation."); //TODO TEST

                _logger.LogWarning($"The PointerFile {pf.RelativeName} is out of sync. Overwriting");

                //Recreate the pointer
                pointerFileInfo.Delete();
                pf = CreatePointerFileIfNotExists(root, pointerFileInfo, manifestHash, creationTimeUtc, lastWriteTimeUtc);
            }

            return pf;
        }
    }
}