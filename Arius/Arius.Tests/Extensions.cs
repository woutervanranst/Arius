using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Arius.Models;

namespace Arius.Tests
{
    static class Extensions
    {
        public static IEnumerable<FileInfo> GetBinaryFiles(this DirectoryInfo directoryInfo)
        {
            //var lcfa = typeof(LocalContentFile).GetCustomAttributes<Arius.Extensions.ExtensionAttribute>().First();
            //return Arius.Extensions.ExtensionAttribute.GetFilesWithExtension(directoryInfo, lcfa);

            return directoryInfo.GetFiles("*", SearchOption.AllDirectories).Where(fi => !fi.FullName.EndsWith(PointerFile.Extension));
        }

        public static IEnumerable<FileInfo> GetPointerFiles(this DirectoryInfo directoryInfo)
        {
            //var lpfa = typeof(LocalPointerFile).GetCustomAttributes<Arius.Extensions.ExtensionAttribute>().First();
            //return Arius.Extensions.ExtensionAttribute.GetFilesWithExtension(directoryInfo, lpfa);

            return directoryInfo.GetFiles("*", SearchOption.AllDirectories).Where(fi => fi.FullName.EndsWith(PointerFile.Extension));
        }

        public static FileInfo GetPointerFileInfo(this FileInfo localContentFileFileInfo)
        {
            return new FileInfo(localContentFileFileInfo.FullName + PointerFile.Extension);
        }
    }
}
