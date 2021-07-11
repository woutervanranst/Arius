using Arius.Core.Extensions;
using Arius.Core.Models;
using Arius.Core.Services;
using System;
using System.IO;

namespace Arius.Core.Tests
{
    static class FileInfoExtensions
    {
        public static FileInfo GetPointerFileInfoFromBinaryFile(this FileInfo binaryFile)
        {
            if (binaryFile.IsPointerFile()) throw new ArgumentException("this is not a BinaryFile");

            return new FileInfo(binaryFile.FullName + PointerFile.Extension);
        }
    }
}
