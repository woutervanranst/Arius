using System;
using System.IO;
using System.Reflection;
using Arius.Extensions;
using Arius.Models;

namespace Arius.Services
{
    internal static class PointerService2
    {
        public static string GetPointerFileFullName(this BinaryFile f)
        {
            return f.FullName + PointerFile.Extension;
        }
        /// <summary>
        /// Create a pointer from a BinaryFile
        /// </summary>
        public static PointerFile CreatePointerFile(this BinaryFile f)
        {
            var pointerFileInfo = new FileInfo(f.GetPointerFileFullName());

            if (pointerFileInfo.Exists)
                throw new ArgumentException("The Pointer file already exists"); //TODO i  expect issies here when the binnary is changed?

            if (!pointerFileInfo.Directory!.Exists)
                pointerFileInfo.Directory.Create();

            File.WriteAllText(pointerFileInfo.FullName, f.Hash!.Value);

            pointerFileInfo.CreationTimeUtc = File.GetCreationTimeUtc(f.FullName);
            pointerFileInfo.LastWriteTimeUtc = File.GetLastWriteTimeUtc(f.FullName);

            return new PointerFile(pointerFileInfo, f.Hash);
        }

        /// <summary>
        /// Create a pointer from a PointerFileEntry
        /// </summary>
        public static PointerFile CreatePointerFile(DirectoryInfo root, PointerFileEntry pfe, Manifest2 manifestFile)
        {
            //return CreatePointerFile(root, root.GetPointerFileInfo(pfe), manifestFile, pfe.CreationTimeUtc!.Value, pfe.LastWriteTimeUtc!.Value);
            throw new NotImplementedException();
        }
        
    }
}