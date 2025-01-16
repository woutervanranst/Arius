using System;
using Zio;

namespace ZioFileSystem.AzureBlobStorage;

public static class FileEntryExtensions
{
    public static string ConvertPathToInternal(this FileEntry f)
    {
        return f.FileSystem.ConvertPathToInternal(f.Path);
    }
}

public static class UPathExtensions
{
    public static bool IsPointerFile(this UPath p)
    {
        return p.GetName().EndsWith(PointerFile.PointerFileExtension, StringComparison.OrdinalIgnoreCase);
    }
}