using System;
using System.IO;

namespace FileSystem.Local;

public interface IFile
{
    FullNamePathSegment FullNamePath { get; }
    string? Path { get; }
    string Name { get; }
    bool Exists { get; }

    /// <summary>
    /// Get the CreationTime of the file in UTC. Null if the file does not exist
    /// </summary>
    DateTime? CreationTimeUtc { get; set; }

    /// <summary>
    /// Get the LastWriteTime of the file in UTC. Null if the file does not exist
    /// </summary>
    DateTime? LastWriteTimeUtc { get; set; }

    //bool IsPointerFile { get; }
    //bool IsBinaryFile { get; }

    //string BinaryFileFullName { get; }
    //string PointerFileFullName { get; }

    long Length { get; }

    //string       GetRelativeName(string relativeTo);
    //string       GetRelativeNamePlatformNeutral(string relativeTo);
    //string       GetRelativeName(DirectoryInfo relativeTo);
    //string       GetRelativeNamePlatformNeutral(DirectoryInfo relativeTo);
    //IPointerFile GetPointerFile(DirectoryInfo root);
    //IBinaryFile GetBinaryFile(DirectoryInfo root);

    Stream OpenRead();
    Stream OpenWrite();

    IFile CopyTo(NamePathSegment destinationName);
    IFile CopyTo(IFile destination);
    void Delete();
}