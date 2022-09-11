using System.IO;

namespace Arius.Core.BehaviorTests.Extensions;

static class DirectoryInfoExtensions
{
    /// <summary>
    /// Empty the directory
    /// </summary>
    /// <param name="dir"></param>
    public static void Clear(this DirectoryInfo dir)
    {
        if (dir.Exists) dir.Delete(true);
        dir.Create();
    }
}