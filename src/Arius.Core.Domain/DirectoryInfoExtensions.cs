namespace WouterVanRanst.Utils;

public static class IDirectoryInfoExtensions
{
    public static string GetFullName(this DirectoryInfo directoryInfo, string fileName)
    {
        return Path.Combine(directoryInfo.FullName, fileName);
    }
}