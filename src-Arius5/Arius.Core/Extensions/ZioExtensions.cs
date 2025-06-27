using Arius.Core.Models;
using WouterVanRanst.Utils.Extensions;
using Zio;

namespace Arius.Core.Extensions;

internal static class FileEntryExtensions
{
    public static string ConvertPathToInternal(this FileEntry fe) 
        => fe.FileSystem.ConvertPathToInternal(fe.Path);

    public static bool IsPointerFile(this FileEntry fe) 
        => fe.Path.IsPointerFilePath();
}

internal static class UPathExtensions
{
    public static bool IsPointerFilePath(this UPath p) 
        => p.GetName().EndsWith(PointerFile.Extension, StringComparison.OrdinalIgnoreCase);

    public static UPath GetPointerFilePath(this UPath binaryFilePath)
    {
        if (binaryFilePath.IsPointerFilePath())
            throw new ArgumentException("Path is not a PointerFile path", nameof(binaryFilePath));

        return binaryFilePath.ChangeExtension($"{binaryFilePath.GetExtensionWithDot()}{PointerFile.Extension}");
    }

    public static UPath GetBinaryFilePath(this UPath pointerFilePath)
    {
        if (!pointerFilePath.IsPointerFilePath())
            throw new ArgumentException("Path is not a BinaryFile path", nameof(pointerFilePath));
        
        return pointerFilePath.RemoveSuffix(PointerFile.Extension);
    }

    public static UPath RemoveSuffix(this UPath p, string value) 
        => new(p.FullName.RemoveSuffix(value, StringComparison.OrdinalIgnoreCase));
}

internal static class DirectoryInfoExtensions
{
    public static UPath ToUPath(this DirectoryInfo directoryInfo)
    {
        return new UPath(directoryInfo.FullName);
    }
}