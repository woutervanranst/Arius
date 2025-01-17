using System;
using System.Collections.Generic;
using System.IO;
using WouterVanRanst.Utils.Extensions;
using Zio;

namespace ZioFileSystem.AzureBlobStorage;

public static class FileEntryExtensions
{
    public static string ConvertPathToInternal(this FileEntry fe) 
        => fe.FileSystem.ConvertPathToInternal(fe.Path);

    public static bool IsPointerFile(this FileEntry fe) 
        => fe.Path.IsPointerFilePath();
}

public static class UPathExtensions
{
    public static bool IsPointerFilePath(this UPath p) 
        => p.GetName().EndsWith(PointerFile.Extension, StringComparison.OrdinalIgnoreCase);

    public static UPath GetPointerFilePath(this UPath binaryFilePath)
    {
        if (binaryFilePath.IsPointerFilePath())
            throw new ArgumentException("Path is not a PointerFile path", nameof(binaryFilePath));

        return binaryFilePath.ChangeExtension($"{binaryFilePath.GetExtensionWithDot()}{PointerFile.Extension}");
    }

    public static UPath GetBinaryFilePath(this UPath pointerFilePath)
    {
        if (!pointerFilePath.IsPointerFilePath())
            throw new ArgumentException("Path is not a BinaryFile path", nameof(pointerFilePath));
        
        return pointerFilePath.RemoveSuffix(PointerFile.Extension);
    }

    public static UPath RemoveSuffix(this UPath p, string value) 
        => new(p.FullName.RemoveSuffix(value, StringComparison.OrdinalIgnoreCase));
}

public static class IFileSystemExtensions
{
    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase) { "@eaDir", "eaDir", "SynoResource" };
    private static readonly HashSet<string> ExcludedFiles = new(StringComparer.OrdinalIgnoreCase) { "autorun.ini", "thumbs.db", ".ds_store" };


    public static IEnumerable<FilePair> EnumerateFilePairs(this IFileSystem fs, DirectoryEntry directory)
    {
        foreach (var fe in EnumerateFiles(directory))
        {
            if (fe.IsPointerFile())
            {
                // this is a PointerFile
                var pf = PointerFile.FromFileEntry(fe);

                if (pf.GetBinaryFile() is { Exists: true } bf)
                {
                    // 1. BinaryFile exists too
                    yield return new(pf, bf);
                }
                else
                {
                    // 2. BinaryFile does not exist
                    yield return new (pf, null);
                }
            }
            else
            {
                // this is a BinaryFile
                var bf = BinaryFile.FromFileEntry(fe);

                if (bf.GetPointerFile() is { Exists: true } pf)
                {
                    // 3. PointerFile exists too -- DO NOT YIELD ANYTHING; this pair has been yielded in (1)
                    continue;
                }
                else
                {
                    // 4. PointerFile does not exist
                    yield return new(null, bf);
                }
            }
        }
    }

    public static IEnumerable<FileEntry> EnumerateFiles(DirectoryEntry directory)
    {
        if (ShouldSkipDirectory(directory))
        {
            //logger.LogWarning("Skipping directory {directory} as it is hidden, system, or excluded", directory.FullName);
            yield break;
        }

        foreach (var fe in directory.EnumerateFiles())
        {
            if (ShouldSkipFile(fe))
            {
                //logger.LogWarning("Skipping file {file} as it is hidden, system, or excluded", fi.FullName);
                continue;
            }

            yield return fe;
        }

        foreach (var subDir in directory.EnumerateDirectories())
        {
            foreach (var file in EnumerateFiles(subDir))
            {
                yield return file;
            }
        }

        yield break;


        static bool ShouldSkipDirectory(DirectoryEntry dir) =>
            (dir.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0 ||
            ExcludedDirectories.Contains(dir.Name);

        static bool ShouldSkipFile(FileEntry file) =>
            (file.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0 ||
            ExcludedFiles.Contains(Path.GetFileName(file.FullName));
    }
}