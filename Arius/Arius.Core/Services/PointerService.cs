using System;
using System.IO;
using Arius.Core.Extensions;
using Arius.Core.Models;
using Arius.Core.Repositories;
using Microsoft.Extensions.Logging;

namespace Arius.Core.Services
{
    internal class PointerService
    {
        public PointerService(ILogger<PointerService> logger, IHashValueProvider hvp)
        {
            this.logger = logger;
            this.hvp = hvp;
        }

        private readonly ILogger<PointerService> logger;
        private readonly IHashValueProvider hvp;


        /// <summary>
        /// Create a pointer from a BinaryFile
        /// </summary>
        public PointerFile CreatePointerFileIfNotExists(BinaryFile bf)
        {
            var target = new FileInfo(GetPointerFileFullName(bf));

            return CreatePointerFileIfNotExists(
                target,
                bf.Root,
                bf.Hash,
                File.GetCreationTimeUtc(bf.FullName),
                File.GetLastWriteTimeUtc(bf.FullName));
        }

        /// <summary>
        /// Create a pointer from a PointerFileEntry
        /// </summary>
        public PointerFile CreatePointerFileIfNotExists(DirectoryInfo root, PointerFileEntry pfe)
        {
            var target = new FileInfo(Path.Combine(root.FullName, pfe.RelativeName));

            return CreatePointerFileIfNotExists(
                target,
                root,
                pfe.ManifestHash,
                pfe.CreationTimeUtc!.Value,
                pfe.LastWriteTimeUtc!.Value);
        }

        private PointerFile CreatePointerFileIfNotExists(FileInfo target, DirectoryInfo root, ManifestHash manifestHash, DateTime creationTimeUtc, DateTime lastWriteTimeUtc)
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

                logger.LogInformation($"Created PointerFile '{target.GetRelativeName(root)}'");
            }

            var pf = new PointerFile(root, target);

            //Check whether the contents of the PointerFile are correct / is it a valid POinterFile / does the hash it refer to match the manifestHash (eg. not in the case of 0 bytes or ...)
            if (!pf.Hash.Equals(manifestHash))
            {
                //throw new ApplicationException($"The PointerFile {pf.RelativeName} is out of sync. Delete the file and restart the operation."); //TODO TEST

                logger.LogWarning($"The PointerFile {pf.RelativeName} is broken. Overwriting");

                //Recreate the pointer
                target.Delete();
                pf = CreatePointerFileIfNotExists(target, root, manifestHash, creationTimeUtc, lastWriteTimeUtc);
            }

            return pf;
        }


        









        /// <summary>
        /// Get the PointerFile for the given FileInfo with the given root.
        /// If the FileInfo is for a PointerFile, return the PointerFile.
        /// If the FileInfo is for a BinaryFile, return the equivalent (in name and LastWriteTime) PointerFile, if it exists.
        /// If it does not exist, return null.
        /// </summary>
        public PointerFile GetPointerFile(DirectoryInfo root, FileInfo fi)
        {
            if (fi.IsPointerFile())
            {
                return new PointerFile(root, fi);
            }
            else
            {
                var pfi = new FileInfo(GetPointerFileFullName(fi.FullName));

                if (!pfi.Exists || pfi.LastWriteTimeUtc != fi.LastWriteTimeUtc)
                    return null;

                return new PointerFile(root, pfi);
            }
        }

        /// <summary>
        /// Get the PointerFile for the given FileInfo assuming it is in the root.
        /// If the FileInfo is for a PointerFile, return the PointerFile.
        /// If the FileInfo is for a BinaryFile, return the equivalent (in name and LastWriteTime) PointerFile, if it exists.
        /// If it does not exist, return null.
        /// </summary>
        public PointerFile GetPointerFile(FileInfo fi) => GetPointerFile(fi.Directory, fi);

        /// <summary>
        /// Get the equivalent (in name and LastWriteTime) PointerFile if it exists.
        /// If it does not exist, return null.
        /// </summary>
        /// <returns></returns>
        public PointerFile GetPointerFile(BinaryFile bf)
        {
            var pfi = new FileInfo(GetPointerFileFullName(bf));

            if (!pfi.Exists || pfi.LastWriteTimeUtc != File.GetLastWriteTimeUtc(bf.FullName))
                return null;

            return new PointerFile(bf.Root, pfi);
        }










        //public static PointerFile GetPointerFile(string binaryFileFullName)
        //{
        //    var pfi = new FileInfo(GetPointerFileFullName(binaryFileFullName));

        //    return GetPointerFile(pfi);
        //}

        //private static PointerFile GetPointerFile(FileInfo pfi)
        //{
        //    if (!pfi.Exists || pfi.LastWriteTimeUtc != File.GetLastWriteTimeUtc(bf.FullName))
        //        return null;

        //    return new PointerFile(bf.Root, pfi);
        //}

        /// <summary>
        /// Get the PointerFile corresponding to the PointerFileEntry, if it exists.
        /// If it does not, return null
        /// </summary>
        /// <param name="pfe"></param>
        /// <returns></returns>
        public PointerFile GetPointerFile(DirectoryInfo root, PointerFileEntry pfe)
        {
            var pfi = new FileInfo(GetPointerFileFullName(root, pfe));

            if (!pfi.Exists) //TODO check op LastWriteTimeUtc ook?
                return null;
            
            return new PointerFile(root, pfi);
        }

        private static string GetPointerFileFullName(BinaryFile bf) => GetPointerFileFullName(bf.FullName);
        private static string GetPointerFileFullName(string binaryFileFullName) => $"{binaryFileFullName}{PointerFile.Extension}";
        private static string GetPointerFileFullName(DirectoryInfo root, PointerFileEntry pfe) => Path.Combine(root.FullName, pfe.RelativeName);


        /// <summary>
        /// Get the local BinaryFile for this pointer if it exists.
        /// If it does not exist, return null.
        /// </summary>
        /// <param name="pf"></param>
        /// <param name="ensureCorrectHash">If we find an existing BinaryFile, calculate the hash and ensure it is correct</param>
        /// <returns></returns>
        public BinaryFile GetBinaryFile(PointerFile pf, bool ensureCorrectHash)
        {
            var bfi = new FileInfo(GetBinaryFileFullName(pf));

            return GetBinaryFile(pf.Root, bfi, pf.Hash, ensureCorrectHash);
        }

        public BinaryFile GetBinaryFile(DirectoryInfo root, PointerFileEntry pfe, bool ensureCorrectHash)
        {
            var bfi = new FileInfo(GetBinaryFileFullname(root, pfe));

            return GetBinaryFile(root, bfi, pfe.ManifestHash, ensureCorrectHash);
        }

        private BinaryFile GetBinaryFile(DirectoryInfo root, FileInfo bfi, ManifestHash manifestHash, bool ensureCorrectHash)
        {
            if (!bfi.Exists)
                return null;

            if (ensureCorrectHash)
            {
                if (manifestHash != hvp.GetManifestHash(bfi.FullName))
                    throw new InvalidOperationException($"The existing BinaryFile {bfi.FullName} is out of sync (invalid hash) with the PointerFile. Delete the BinaryFile and try again.");
            }

            return new BinaryFile(root, bfi, manifestHash);
        }

        private static string GetBinaryFileFullname(DirectoryInfo root, PointerFileEntry pfe) => GetBinaryFileFullName(GetPointerFileFullName(root, pfe));
        private static string GetBinaryFileFullName(PointerFile pf) => GetBinaryFileFullName(pf.FullName);
        private static string GetBinaryFileFullName(string pointerFileFullName) => pointerFileFullName.TrimEnd(PointerFile.Extension);






        public FileInfo GetBinaryFileInfo(PointerFile pf)
        {
            return new FileInfo(pf.FullName.TrimEnd(PointerFile.Extension));
        }

    }
}