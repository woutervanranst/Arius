using Zio;

namespace ZioFileSystem.AzureBlobStorage;

public static class FileEntryExtensions
{
    public static string ConvertPathToInternal(this FileEntry f)
    {
        return f.FileSystem.ConvertPathToInternal(f.Path);
    }
}