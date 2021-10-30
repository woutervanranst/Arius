using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Arius.Core.Extensions;

static class DirectoryInfoExtensions
{
    public static FileInfo[] TryGetFiles(this DirectoryInfo d, string searchPattern)
    {
        try
        {
            return d.GetFiles(searchPattern, SearchOption.AllDirectories);
        }
        catch (UnauthorizedAccessException)
        {
            return Array.Empty<FileInfo>();
        }
        catch (DirectoryNotFoundException)
        {
            return Array.Empty<FileInfo>();
        }
    }


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
            if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory, false);
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


    public static IEnumerable<FileInfo> GetBinaryFileInfos(this DirectoryInfo di, ILogger logger = default)
    {
        return di.GetAllFileInfos(logger).Where(fi => !fi.IsPointerFile());
    }

    public static IEnumerable<FileInfo> GetPointerFileInfos(this DirectoryInfo di, ILogger logger = default)
    {
        return di.GetAllFileInfos(logger).Where(fi => fi.IsPointerFile());
    }

    public static IEnumerable<FileInfo> GetAllFileInfos(this DirectoryInfo di, ILogger logger = default)
    {
        //return di.GetFiles("*", SearchOption.AllDirectories);

        foreach (var file in di.GetFiles())
        {
            if (IsHiddenOrSystem(file))
            {
                logger?.LogDebug($"Skipping file {file.FullName} as it is SYSTEM or HIDDEN");
                continue;
            }
            else if (IsIgnoreFile(file))
            {
                logger?.LogDebug($"Ignoring file {file.FullName}");
                continue;
            }
            else
            {
                yield return file;
            }
        }

        foreach (var dir in di.GetDirectories())
        {
            if (IsHiddenOrSystem(dir))
            {
                logger?.LogDebug($"Skipping directory {dir.FullName} as it is SYSTEM or HIDDEN");
                continue;
            }

            foreach (var f in GetAllFileInfos(dir, logger))
                yield return f;
        }
    }

    private static bool IsHiddenOrSystem(DirectoryInfo d)
    {
        if (d.Name == "@eaDir") //synology internals -- ignore
            return true;

        return IsHiddenOrSystem(d.Attributes);

    }
    private static bool IsHiddenOrSystem(FileInfo fi, ILogger logger = default)
    {
        if (fi.FullName.Contains("eaDir") ||
            fi.FullName.Contains("SynoResource"))
            //fi.FullName.Contains("@")) // commenting out -- email adresses are not weird
            logger?.LogWarning("WEIRD FILE: " + fi.FullName);

        return IsHiddenOrSystem(fi.Attributes);
    }
    private static bool IsHiddenOrSystem(FileAttributes attr)
    {
        return (attr & FileAttributes.System) != 0 || (attr & FileAttributes.Hidden) != 0;
    }
    private static bool IsIgnoreFile(FileInfo fi)
    {
        var lowercaseFilename = fi.Name.ToLower();

        return lowercaseFilename.Equals("autorun.ini") ||
               lowercaseFilename.Equals("thumbs.db") ||
               lowercaseFilename.Equals(".ds_store");
    }
}