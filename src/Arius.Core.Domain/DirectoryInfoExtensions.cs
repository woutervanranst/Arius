using System.IO.Abstractions;

namespace WouterVanRanst.Utils;

public static class DirectoryInfoExtensions
{
    public static string GetFullName(this IDirectoryInfo directoryInfo, string fileName)
    {
        return Path.Combine(directoryInfo.FullName, fileName);
    }
}