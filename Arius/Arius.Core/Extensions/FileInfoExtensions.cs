using Arius.Core.Models;
using System;
using System.IO;

namespace Arius.Core.Extensions;

public static class FileInfoExtensions
{
    internal static bool IsPointerFile(this FileInfo fi) => fi.Name.EndsWith(PointerFile.Extension, StringComparison.CurrentCultureIgnoreCase);
}