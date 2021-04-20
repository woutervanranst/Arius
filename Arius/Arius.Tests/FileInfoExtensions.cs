using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Arius.Extensions;
using Arius.Models;

namespace Arius.Tests
{
    static class FileInfoExtensions
    {
        public static PointerFile GetPointerFile(this FileInfo binaryFile)
        {
            if (binaryFile.IsPointerFile()) throw new ArgumentException("this is not a BinaryFile");

            return new PointerFile(binaryFile.Directory, binaryFile.GetPointerFileInfo());
        }

        public static FileInfo GetPointerFileInfo(this FileInfo binaryFile)
        {
            if (binaryFile.IsPointerFile()) throw new ArgumentException("this is not a BinaryFile");

            return new FileInfo(binaryFile.FullName + PointerFile.Extension);
        }
    }
}
