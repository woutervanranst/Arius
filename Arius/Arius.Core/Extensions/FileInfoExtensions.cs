using Arius.Core.Models;
using System;
using System.IO;

namespace Arius.Core.Extensions;

public static class FileInfoExtensions
{
    internal static bool IsPointerFile(this FileInfo fi) => fi.Name.EndsWith(PointerFile.Extension, StringComparison.CurrentCultureIgnoreCase);

    public static FileInfo GetPointerFileInfoFromBinaryFile(this FileInfo binaryFileInfo)
    {
        if (binaryFileInfo.IsPointerFile()) throw new ArgumentException("this is not a BinaryFile");

        return new FileInfo(binaryFileInfo.FullName + PointerFile.Extension);
    }
}