using Zio;
using Zio.FileSystems;

namespace Arius.Core.Repositories;

public interface IFileSystem
{
    IEnumerable<FileEntry> EnumerateFileEntries(UPath path, string searchPattern, SearchOption searchOption);
    bool FileExists(UPath path);
    UPath ConvertPathToInternal(UPath path);
}