using Arius.Core.Domain.Storage.FileSystem;
using File = Arius.Core.Domain.Storage.FileSystem.File;

namespace Arius.Core.Infrastructure.Extensions;

public static class DirectoryInfoExtensions
{
    public static IFile GetFile(this DirectoryInfo directoryInfo, string relativeName)
    {
        return File.FromRelativeName(directoryInfo, relativeName);
    }
}