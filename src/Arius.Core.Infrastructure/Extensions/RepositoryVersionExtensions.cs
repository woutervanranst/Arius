using Arius.Core.Domain.Storage;

namespace Arius.Core.Infrastructure.Extensions;

public static class RepositoryVersionExtensions
{
    public static string GetFileSystemName(this RepositoryVersion version)
    {
        return version.Name.Replace(":", "");
    }
}