namespace Arius.Core.BehaviorTests;

internal static class StringPathExtensions
{
    public static string FromWindowsPathToPlatformPath(this string path)
    {
        return path.Replace(@"\\", @"\").Replace('\\', Path.DirectorySeparatorChar);
    }
}