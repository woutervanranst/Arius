//using IFileSystem = System.IO.Abstractions.IFileSystem;
//using FileSystem = System.IO.FileSystem;

using System;
using System.IO;
using TIO = System.IO.Abstractions;

using System.IO.Abstractions;

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

public class AriusFileSystem
{
    public static IFileSystem Instance { get; } = new TIO.FileSystem();
}

public record FullFileName
{

}






public class AriusFile : IFile
{
    public AriusFile(FullName fullName)
    {
        FullName = fullName;
        //AriusFileSystem.Instance.File

        //new FileSystem()
        //System.IO.Abstractions.IFileSystem
        //System.IO.Abstractions.file.IFile
    }

    public FullName FullName { get; }
    public string? Path { get; }
    public string Name { get; }
    public bool Exists => AriusFileSystem.Instance.File.Exists(FullName);

    public DateTime? CreationTimeUtc
    {
        get
        {
            AriusFileSystem.Instance.File.GetCreationTime()
        }
        set
        {

        }
    }
    public DateTime? LastWriteTimeUtc { get; set; }
    public long Length { get; }
    public Stream OpenRead()
    {
        throw new NotImplementedException();
    }

    public Stream OpenWrite()
    {
        throw new NotImplementedException();
    }

    public IFile CopyTo(string destinationName)
    {
        throw new NotImplementedException();
    }

    public IFile CopyTo(IFile destination)
    {
        throw new NotImplementedException();
    }

    public void Delete()
    {
        throw new NotImplementedException();
    }
}

//public interface IStateDatabaseFile : IFile
//{
//    public static readonly string Extension = ".ariusdb";
//    StateVersion Version { get; }
//}

//public interface IRelativeFile
//{
//    public DirectoryInfo Root { get; }

//    public string RelativeName { get; }
//    string RelativeNamePlatformNeutral { get; }

//    // TODO other properties
//}

//public interface IPointerFile : IFile, IRelativeFile
//{
//    public static readonly string Extension = ".pointer.arius";

//    public string BinaryFileRelativeName { get; }
//    public string BinaryFileRelativeNamePlatformNeutral { get; }

//    IPointerFile GetPointerFile();
//    IBinaryFile GetBinaryFile();
//}

//public interface IBinaryFile : IFile, IRelativeFile
//{
//    public string PointerFileRelativeName { get; }
//    public string PointerFileRelativeNamePlatformNeutral { get; }

//    IPointerFile GetPointerFile();
//    IBinaryFile GetBinaryFile();
//}

//public interface IWithHash
//{
//    public Hash Hash { get; }
//}

//public interface IBinaryFileWithHash : IBinaryFile, IWithHash
//{
//    IPointerFileWithHash GetPointerFileWithHash();
//    IBinaryFileWithHash GetBinaryFileWithHash();
//}

//public interface IPointerFileWithHash : IPointerFile, IWithHash
//{
//    IPointerFileWithHash GetPointerFileWithHash();
//    IBinaryFileWithHash GetBinaryFileWithHash();
//}

//public interface IFilePair : IRelativeFile
//{
//    IPointerFile PointerFile { get; }
//    IBinaryFile BinaryFile { get; }
//    FilePairType Type { get; }

//    bool IsBinaryFileWithPointerFile { get; }
//    bool IsPointerFileOnly { get; }
//    bool IsBinaryFileOnly { get; }

//    bool HasExistingPointerFile { get; }
//    bool HasExistingBinaryFile { get; }
//}

//public interface IFilePairWithHash : IFilePair, IWithHash
//{
//    new IPointerFileWithHash PointerFile { get; }
//    new IBinaryFileWithHash BinaryFile { get; }
//}

//public enum FilePairType
//{
//    PointerFileOnly,
//    BinaryFileOnly,
//    BinaryFileWithPointerFile,
//    None
//}



