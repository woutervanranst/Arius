using Arius.Core.Shared.FileSystem;
using WouterVanRanst.Utils.Extensions;
using Zio;
using Zio.FileSystems;

namespace Arius.Core.Shared.Extensions;

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

internal static class FileSystemExtensions
{
    //public static FilePair FromBinaryFilePath(this IFileSystem fs, UPath binaryFilePath) 
    //    => FilePair.FromBinaryFilePath(fs, binaryFilePath);

    /// <summary>
    /// Recursively unwraps nested filesystems to find if any underlying filesystem is of the specified type T.
    /// </summary>
    public static bool HasUnderlyingFileSystemOfType<T>(this IFileSystem fileSystem) where T : class, IFileSystem
    {
        // First check the wrapped filesystems before checking the current one
        IFileSystem? nextFs = null;
        
        // Check if it's a ComposeFileSystem (like FilePairFileSystem) which wraps another filesystem
        if (fileSystem is ComposeFileSystem composeFs)
        {
            nextFs = composeFs.Fallback;
        }
        // Check if it's a SubFileSystem which wraps another filesystem
        else if (fileSystem is SubFileSystem subFs)
        {
            nextFs = subFs.Fallback;
        }
        
        // Recursively check the wrapped filesystem first
        if (nextFs != null && nextFs.HasUnderlyingFileSystemOfType<T>())
            return true;
            
        // Finally check if the current filesystem is of the specified type
        return fileSystem is T;
    }
}