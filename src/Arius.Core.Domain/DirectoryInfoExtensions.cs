namespace WouterVanRanst.Utils;

public static class IDirectoryInfoExtensions
{
    public static string GetFileFullName(this DirectoryInfo directoryInfo, string fileName)
    {
        return Path.Combine(directoryInfo.FullName, fileName);
    }
}