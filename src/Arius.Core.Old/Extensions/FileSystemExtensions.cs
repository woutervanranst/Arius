using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Arius.Core.Models;

namespace Arius.Core.Extensions;

internal static class FileInfoExtensions
{
    // TODO refactor & put in PointerFileInfo IsPointerFile()
    internal static bool IsPointerFile(this FileInfo fi) => fi.Name.EndsWith(PointerFileInfo.Extension, StringComparison.CurrentCultureIgnoreCase);
}



internal static class FileInfoBaseExtensions
{
    public static string GetRelativeName(this FileInfoBase fi, DirectoryInfo root) => Path.GetRelativePath(root.FullName, fi.FullName);
}



internal static class DirectoryInfoExtensions
{
    public static void DeleteEmptySubdirectories(this DirectoryInfo parentDirectory, bool includeSelf = false)
    {
        DeleteEmptySubdirectories(parentDirectory.FullName);

        if (includeSelf)
            if (!parentDirectory.EnumerateFileSystemInfos().Any())
                parentDirectory.Delete();
    }
    private static void DeleteEmptySubdirectories(string parentDirectory)
    {
        Parallel.ForEach(Directory.GetDirectories(parentDirectory), directory =>
        {
            DeleteEmptySubdirectories(directory);
            if (!Directory.EnumerateFileSystemEntries(directory).Any())
                Directory.Delete(directory, false);
        });
    }


    public static void CopyTo(this DirectoryInfo sourceDir, string targetDir)
    {
        sourceDir.FullName.CopyTo(targetDir);
    }
    public static void CopyTo(this DirectoryInfo sourceDir, DirectoryInfo targetDir)
    {
        sourceDir.FullName.CopyTo(targetDir.FullName);
    }
    private static void CopyTo(this string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);

        foreach (var file in Directory.GetFiles(sourceDir))
            File.Copy(file, Path.Combine(targetDir, Path.GetFileName(file)));

        foreach (var directory in Directory.GetDirectories(sourceDir))
            directory.CopyTo(Path.Combine(targetDir, Path.GetFileName(directory)));
    }


    public static bool IsEmpty(this DirectoryInfo di)
    {
        return !di.GetFileSystemInfos().Any();
    }
}



internal static class PathExtensions
{
    public static string ToPlatformNeutralPath(this string platformSpecificPath)
    {
        if (Path.DirectorySeparatorChar == PLATFORM_NEUTRAL_DIRECTORY_SEPARATOR_CHAR)
            return platformSpecificPath;

        return platformSpecificPath.Replace(Path.DirectorySeparatorChar, PLATFORM_NEUTRAL_DIRECTORY_SEPARATOR_CHAR);

        //if (platformSpecific is null)
        //    return null;
        //if (Path.DirectorySeparatorChar == PLATFORM_NEUTRAL_DIRECTORY_SEPARATOR_CHAR)
        //    return platformSpecific;
        //return platformSpecific with { RelativeName = platformSpecific.RelativeName.Replace(Path.DirectorySeparatorChar, PLATFORM_NEUTRAL_DIRECTORY_SEPARATOR_CHAR) };
    }

    public static string ToPlatformSpecificPath(this string platformNeutralPath)
    {
        // TODO UNIT TEST for linux pointers (already done if run in the github runner?

        if (Path.DirectorySeparatorChar == PLATFORM_NEUTRAL_DIRECTORY_SEPARATOR_CHAR)
            return platformNeutralPath;

        return platformNeutralPath.Replace(PLATFORM_NEUTRAL_DIRECTORY_SEPARATOR_CHAR, Path.DirectorySeparatorChar);

        //if (platformNeutral is null)
        //    return null;
        //if (Path.DirectorySeparatorChar == PLATFORM_NEUTRAL_DIRECTORY_SEPARATOR_CHAR)
        //    return platformNeutral;
        //return platformNeutral with { RelativeName = platformNeutral.RelativeName.Replace(PLATFORM_NEUTRAL_DIRECTORY_SEPARATOR_CHAR, Path.DirectorySeparatorChar) };
    }

    internal const char PLATFORM_NEUTRAL_DIRECTORY_SEPARATOR_CHAR = '/';
}