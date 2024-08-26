namespace WouterVanRanst.Utils;

public static class DirectoryInfoExtensions
{
    public static string GetFullFileName(this DirectoryInfo directoryInfo, string fileName)
    {
        return Path.Combine(directoryInfo.FullName, fileName);
    }

}