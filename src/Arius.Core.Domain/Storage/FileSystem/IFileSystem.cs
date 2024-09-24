namespace Arius.Core.Domain.Storage.FileSystem;

public interface IFileSystem
{
    public IEnumerable<IFile>     EnumerateFiles(DirectoryInfo directory);
    public IEnumerable<IFilePair> EnumerateFilePairs(DirectoryInfo directory);
}

public interface IFile
{
    string  FullName                { get; }
    //string  FullNamePlatformNeutral { get; }
    string? Path                    { get; }
    //string? PathPlatformNeutral     { get; }
    string  Name                    { get; }
    bool    Exists                  { get; }

    DateTime? CreationTimeUtc { get; set; }

    DateTime? LastWriteTimeUtc { get; set; }

    bool   IsPointerFile                      { get; }
    bool   IsBinaryFile                       { get; }
    string BinaryFileFullName                 { get; }
    //string BinaryFileFullNamePlatformNeutral  { get; }
    string PointerFileFullName                { get; }
    //string PointerFileFullNamePlatformNeutral { get; }
    long   Length                             { get; }

    //string       GetRelativeName(string relativeTo);
    //string       GetRelativeNamePlatformNeutral(string relativeTo);
    //string       GetRelativeName(DirectoryInfo relativeTo);
    //string       GetRelativeNamePlatformNeutral(DirectoryInfo relativeTo);
    IPointerFile GetPointerFile(DirectoryInfo root);
    IBinaryFile  GetBinaryFile(DirectoryInfo root);

    Stream OpenRead();
    Stream OpenWrite();

    IFile CopyTo(string destinationName);
    void  Delete();
}


public interface IStateDatabaseFile : IFile
{
    public static readonly string Extension     = ".ariusdb";
    RepositoryVersion             Version { get; }
}


public interface IRelativeFile
{
    public DirectoryInfo Root { get; }

    public string RelativeName                { get; }
    string        RelativeNamePlatformNeutral { get; }

    // TODO other properties
}

public interface IPointerFile : IFile, IRelativeFile
{
    public static readonly string Extension = ".pointer.arius";

    public string BinaryFileRelativeName                { get; }
    public string BinaryFileRelativeNamePlatformNeutral { get; }
}

public interface IBinaryFile : IFile, IRelativeFile
{
    public string PointerFileRelativeName                { get; }
    public string PointerFileRelativeNamePlatformNeutral { get; }
}

public interface IWithHash
{
    public Hash Hash { get; }
}

public interface IBinaryFileWithHash : IBinaryFile, IWithHash
{
}

public interface IPointerFileWithHash : IPointerFile, IWithHash
{
}

public interface IFilePair : IRelativeFile
{
    IPointerFile? PointerFile                 { get; }
    IBinaryFile?  BinaryFile                  { get; }
    FilePairType  Type                        { get; }
    
    bool          IsBinaryFileWithPointerFile { get; }
    bool          IsPointerFileOnly           { get; }
    bool          IsBinaryFileOnly            { get; }
    
    bool          HasPointerFile              { get; }
    bool          HasBinaryFile               { get; }
    
    bool          HasExistingPointerFile      { get; }
    bool          HasExistingBinaryFile       { get; }
}

public interface IFilePairWithHash : IFilePair, IWithHash
{
    new IPointerFileWithHash? PointerFile { get; }
    new IBinaryFileWithHash?  BinaryFile  { get; }
}

public enum FilePairType
{
    PointerFileOnly,
    BinaryFileOnly,
    BinaryFileWithPointerFile,
    None
}