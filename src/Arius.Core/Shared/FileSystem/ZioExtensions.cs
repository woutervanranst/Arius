using Arius.Core.Shared.FileSystem;
using WouterVanRanst.Utils.Extensions;
using Zio;

namespace Arius.Core.Shared.FileSystem;

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


internal static class DirectoryEntryExtensions
{
    /// <summary>
    /// Gets a FileEntry with the given name inside this directory.
    /// NOTE: This file does not necessarily exist.
    /// </summary>
    public static FileEntry GetFileEntry(this DirectoryEntry dir, string fileName)
    {
        if (dir == null)
            throw new ArgumentNullException(nameof(dir));

        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("Filename cannot be empty", nameof(fileName));

        UPath filePath = dir.Path / fileName;
        return new FileEntry(dir.FileSystem, filePath);
    }
}


internal static class FileSystemExtensions
{
    //public static FilePair FromBinaryFilePath(this IFileSystem fs, UPath binaryFilePath) 
    //    => FilePair.FromBinaryFilePath(fs, binaryFilePath);

    public static DateTime GetCreationTimeUtc(this IFileSystem fileSystem, UPath path)
        => fileSystem.GetCreationTime(path).ToUniversalTime();

    public static DateTime GetLastWriteTimeUtc(this IFileSystem fileSystem, UPath path)
        => fileSystem.GetLastWriteTime(path).ToUniversalTime();

    public static void SetCreationTimeUtc(this IFileSystem fileSystem, UPath path, DateTime creationTimeUtc)
        => fileSystem.SetCreationTime(path, creationTimeUtc.ToLocalTime());

    public static void SetLastWriteTimeUtc(this IFileSystem fileSystem, UPath path, DateTime lastWriteTimeUtc)
        => fileSystem.SetLastWriteTime(path, lastWriteTimeUtc.ToLocalTime());
}