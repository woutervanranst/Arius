using System;
using System.IO;
using System.Reflection;
using Arius.CommandLine;
using Arius.Extensions;
using Arius.Models;
using Arius.Repositories;

namespace Arius.Services
{
    internal static class PointerService
    {
        public static string GetPointerFileFullName(this BinaryFile f)
        {
            return f.FullName + PointerFile.Extension;
        }
        /// <summary>
        /// Create a pointer from a BinaryFile
        /// </summary>
        public static PointerFile CreatePointerFileIfNotExists(this BinaryFile f)
        {
            var pointerFileInfo = new FileInfo(f.GetPointerFileFullName());

            if (!pointerFileInfo.Exists)
            {
                if (!pointerFileInfo.Directory!.Exists)
                    pointerFileInfo.Directory.Create();

                File.WriteAllText(pointerFileInfo.FullName, f.Hash!.Value);

                pointerFileInfo.CreationTimeUtc = File.GetCreationTimeUtc(f.FullName);
                pointerFileInfo.LastWriteTimeUtc = File.GetLastWriteTimeUtc(f.FullName);
            }

            return new PointerFile(f.Root, pointerFileInfo, f.Hash);
        }

        /// <summary>
        /// Create a pointer from a PointerFileEntry
        /// </summary>
        public static PointerFile CreatePointerFile(DirectoryInfo root, AzureRepository.PointerFileEntry pfe, string manifestFile)
        {
            //return CreatePointerFile(root, root.GetPointerFileInfo(pfe), manifestFile, pfe.CreationTimeUtc!.Value, pfe.LastWriteTimeUtc!.Value);
            throw new NotImplementedException();
        }
        
    }
}