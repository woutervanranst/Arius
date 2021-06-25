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
            var target = new FileInfo(PointerFile.GetFullName(f));

            return CreatePointerFileIfNotExists(
                target,
                f.Root,
                f.Hash,
                File.GetCreationTimeUtc(f.FullName),
                File.GetLastWriteTimeUtc(f.FullName));
        }

        /// <summary>
        /// Create a pointer from a PointerFileEntry
        /// </summary>
        public PointerFile CreatePointerFileIfNotExists(DirectoryInfo root, AzureRepository.PointerFileEntry pfe)
        {
            var target = new FileInfo(Path.Combine(root.FullName, pfe.RelativeName));

            return CreatePointerFileIfNotExists(
                target,
                root,
                pfe.ManifestHash,
                pfe.CreationTimeUtc!.Value,
                pfe.LastWriteTimeUtc!.Value);
        }

        private PointerFile CreatePointerFileIfNotExists(FileInfo target, DirectoryInfo root, HashValue manifestHash, DateTime creationTimeUtc, DateTime lastWriteTimeUtc)
        {
            if (!target.Exists)
            {
                if (!target.Directory!.Exists)
                    target.Directory.Create();

                File.WriteAllText(target.FullName, manifestHash.Value);

                //FileInfo does not work on Linux according to https://stackoverflow.com/a/17126045/1582323
                //pointerFileInfo.CreationTimeUtc = creationTimeUtc;
                //pointerFileInfo.LastWriteTimeUtc = lastWriteTimeUtc;

                File.SetCreationTimeUtc(target.FullName, creationTimeUtc);
                File.SetLastWriteTimeUtc(target.FullName, lastWriteTimeUtc);

                _logger.LogInformation($"Created PointerFile '{Path.GetRelativePath(root.FullName, target.FullName)}'");
            }

            var pf = new PointerFile(root, target);

            //Check whether the contents of the PointerFile are correct / is it a valid POinterFile / does the hash it refer to match the manifestHash (eg. not in the case of 0 bytes or ...)
            if (!pf.Hash.Equals(manifestHash))
            {
                //throw new ApplicationException($"The PointerFile {pf.RelativeName} is out of sync. Delete the file and restart the operation."); //TODO TEST

                _logger.LogWarning($"The PointerFile {pf.RelativeName} is broken. Overwriting");

                //Recreate the pointer
                target.Delete();
                pf = CreatePointerFileIfNotExists(target, root, manifestHash, creationTimeUtc, lastWriteTimeUtc);
            }

            return pf;
        }
    }
}