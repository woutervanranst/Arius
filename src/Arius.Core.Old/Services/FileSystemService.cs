using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Arius.Core.Extensions;
using Arius.Core.Models;
using Microsoft.Extensions.Logging;

namespace Arius.Core.Services;

public class FileSystemService
{
    private readonly ILogger<FileSystemService> logger;

    public FileSystemService(ILogger<FileSystemService> logger)
    {
        this.logger = logger;
    }

    public IEnumerable<FileInfoBase> GetAllFileInfos(DirectoryInfo di)
    {
        foreach (var fi in di.EnumerateFiles()) //.GetFiles())  // TODO TEST WITH VIRUS
        {
            if (IsHiddenOrSystem(fi))
                logger.LogDebug($"Skipping file {fi.FullName} as it is SYSTEM or HIDDEN");
            else if (IsIgnoreFile(fi))
                logger.LogDebug($"Ignoring file {fi.FullName}");
            else
                yield return GetFileInfo(fi);
        }

        foreach (var dir in di.GetDirectories())
        {
            if (IsHiddenOrSystem(dir))
                logger.LogDebug($"Skipping directory {dir.FullName} as it is SYSTEM or HIDDEN");
            else
                foreach (var ifi in GetAllFileInfos(dir))
                    yield return ifi;
        }
    }

    public IEnumerable<BinaryFileInfo> GetBinaryFileInfos(DirectoryInfo di) => GetAllFileInfos(di).OfType<BinaryFileInfo>();

    public IEnumerable<PointerFileInfo> GetPointerFileInfos(DirectoryInfo di) => GetAllFileInfos(di).OfType<PointerFileInfo>();

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

        if (lowercaseFilename.Equals("arius.config") &&
            fi.Length < 1024 &&
            File.ReadAllLines(fi.FullName)[0].StartsWith("{\"AccountName\":\"")) //TODO h4x0r since on Linux/Docker we cannot set the HIDDEN or SYSTEM attribute so cannot detect the arius.config file to ignore it.
            // related to https://stackoverflow.com/questions/45635937/change-file-permissions-in-mounted-folder-inside-docker-container-on-windows-hos ?
            return true;

        return lowercaseFilename.Equals("autorun.ini") ||
               lowercaseFilename.Equals("thumbs.db") ||
               lowercaseFilename.Equals(".ds_store");
    }


    

    /// <summary>
    /// Get a PointerFileInfo object for a (not necessarily existing) file
    /// If the FileInfo points to a PointerFile, returns the PointerFileInfo
    /// If the FileInfo points to a BinaryFile, returns the corresponding PointerFileInfo
    /// </summary>
    public static PointerFileInfo GetPointerFileInfo(FileInfo fi) => GetPointerFileInfo(GetFileInfo(fi));
    /// <inheritdoc cref="GetPointerFileInfo(FileInfo)"/>
    internal static PointerFileInfo GetPointerFileInfo(BinaryFile bf) => GetPointerFileInfo(bf.FullName);
    /// <inheritdoc cref="GetPointerFileInfo(FileInfo)"/>
    public static PointerFileInfo GetPointerFileInfo(string fileName) => GetPointerFileInfo(GetFileInfo(fileName));
    /// <inheritdoc cref="GetPointerFileInfo(FileInfo)"/>
    internal static PointerFileInfo GetPointerFileInfo(DirectoryInfo root, PointerFileEntry pfe) => GetPointerFileInfo(Path.Combine(root.FullName, pfe.RelativeName));
    public static PointerFileInfo GetPointerFileInfo(DirectoryInfo root, string relativeName) => GetPointerFileInfo(Path.Combine(root.FullName, relativeName));
    /// <inheritdoc cref="GetPointerFileInfo(FileInfo)"/>
    public static PointerFileInfo GetPointerFileInfo(FileInfoBase fib)
    {
        if (fib is PointerFileInfo pfi)
            return pfi;
        else if (fib is BinaryFileInfo bfi)
            return (PointerFileInfo)GetFileInfo(bfi.PointerFileFullName);
        else
            throw new NotImplementedException();
    }


    
    /// <summary>
    /// Get a BinaryFileInfo object for a (not necessarily existing) file
    /// If the FileInfo points to a BinaryFile, returns the BinaryFileInfo
    /// If the FileInfo points to a PointerFile, returns the corresponding BinaryFileInfo
    /// </summary>
    public static BinaryFileInfo GetBinaryFileInfo(FileInfo fi)                              => GetBinaryFileInfo(GetFileInfo(fi));
    /// <inheritdoc cref="GetBinaryFileInfo(FileInfo)"/>
    internal static BinaryFileInfo GetBinaryFileInfo(PointerFile pf)                           => GetBinaryFileInfo(pf.FullName);
    /// <inheritdoc cref="GetBinaryFileInfo(FileInfo)"/>
    internal static BinaryFileInfo GetBinaryFileInfo(DirectoryInfo root, PointerFileEntry pfe) => GetBinaryFileInfo(Path.Combine(root.FullName, pfe.RelativeName));
    public static BinaryFileInfo GetBinaryFileInfo(DirectoryInfo root, string relativeName) => GetBinaryFileInfo(Path.Combine(root.FullName, relativeName));
    /// <inheritdoc cref="GetBinaryFileInfo(FileInfo)"/>
    public static BinaryFileInfo GetBinaryFileInfo(string fileName)                          => GetBinaryFileInfo(GetFileInfo(fileName));
    /// <inheritdoc cref="GetBinaryFileInfo(FileInfo)"/>
    public static BinaryFileInfo GetBinaryFileInfo(FileInfoBase fib)
    {
        if (fib is BinaryFileInfo bfi)
            return bfi;
        else if (fib is PointerFileInfo pfi)
            return (BinaryFileInfo)GetFileInfo(pfi.BinaryFileFullName);
        else
            throw new NotImplementedException();
    }


    public static FileInfoBase GetFileInfo(string fileName)
    {
        return GetFileInfo(new FileInfo(fileName));
    }
    private static FileInfoBase GetFileInfo(FileInfo fi)
    {
        if (fi.IsPointerFile())
            return new PointerFileInfo(fi);
        else
            return new BinaryFileInfo(fi);
    }
}