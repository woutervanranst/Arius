//using System;
//using System.Collections.Generic;
//using System.IO;
//using System.Linq;
//using System.Reflection;
//using System.Text;
//using System.Threading.Tasks;
//using Arius.Models;

//namespace Arius.Tests
//{
//    static class Extensions
//    {
//        public static FileInfo[] GetLocalContentFiles(this DirectoryInfo directoryInfo)
//        {
//            var lcfa = typeof(LocalContentFile).GetCustomAttributes<Arius.Extensions.ExtensionAttribute>().First();
//            return Arius.Extensions.ExtensionAttribute.GetFilesWithExtension(directoryInfo, lcfa);
//        }

//        public static FileInfo[] GetPointerFiles(this DirectoryInfo directoryInfo)
//        {
//            var lpfa = typeof(LocalPointerFile).GetCustomAttributes<Arius.Extensions.ExtensionAttribute>().First();
//            return Arius.Extensions.ExtensionAttribute.GetFilesWithExtension(directoryInfo, lpfa);
//        }

//        public static FileInfo GetPointerFileInfo(this FileInfo localContentFileFileInfo)
//        {
//            return new FileInfo(localContentFileFileInfo.FullName + GetPointerExtension());
//        }

//        public static string GetPointerExtension()
//        {
//            return typeof(LocalPointerFile).GetCustomAttributes<Arius.Extensions.ExtensionAttribute>().First().Extension;
//        }
//    }
//}
