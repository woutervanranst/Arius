using Arius.Core.Domain.Extensions;
using System.Text.Json;

namespace Arius.Core.Domain.Storage.FileSystem;

public interface IFile
{
    string  FullName                { get; }
    //string  FullNamePlatformNeutral { get; }
    string? Path                    { get; }
    string? PathPlatformNeutral     { get; }
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

    string      GetRelativeName(string relativeTo);
    string      GetRelativeNamePlatformNeutral(string relativeTo);
    string      GetRelativeName(DirectoryInfo relativeTo);
    string      GetRelativeNamePlatformNeutral(DirectoryInfo relativeTo);
    PointerFile GetPointerFile(DirectoryInfo root);
    BinaryFile  GetBinaryFile(DirectoryInfo root);

    Stream OpenRead();
    Stream OpenWrite();

    IFile CopyTo(string destinationName);
    void  Delete();
}

public record File : IFile // TODO make internal
{
    private readonly string fullName;
    //protected readonly FileInfo fileInfo;

    protected File(string fullName)
    {
        if (!System.IO.Path.IsPathFullyQualified(fullName))
            throw new ArgumentException($"'{fullName}' is not a fully qualified path");

        this.fullName = fullName;
        //this.fileInfo = new FileInfo(fullName);
    }

    //public static File FromFileInfo(FileInfo fi)             => new(fi);
    public static File FromFullName(string fullName)         => new(fullName);
    public static File FromRelativeName(DirectoryInfo root, string relativeName) => new(System.IO.Path.Combine(root.FullName, relativeName));

    public string  FullName                => fullName;
    //public string  FullNamePlatformNeutral => FullName.ToPlatformNeutralPath();
    public string? Path                    => System.IO.Path.GetDirectoryName(fullName);
    public string? PathPlatformNeutral     => Path?.ToPlatformNeutralPath();
    public string  Name                    => System.IO.Path.GetFileName(fullName);

    public string GetRelativeName(string relativeTo)                       => System.IO.Path.GetRelativePath(relativeTo, fullName);
    public string GetRelativeNamePlatformNeutral(string relativeTo)        => GetRelativeName(relativeTo).ToPlatformNeutralPath();
    public string GetRelativeName(DirectoryInfo relativeTo)                => GetRelativeName(relativeTo.FullName);
    public string GetRelativeNamePlatformNeutral(DirectoryInfo relativeTo) => GetRelativeNamePlatformNeutral(relativeTo.FullName);

    public bool Exists => System.IO.File.Exists(FullName);

    public DateTime? CreationTimeUtc
    {
        get => Exists ? System.IO.File.GetCreationTimeUtc(FullName) : null;
        set
        {
            if (Exists)
                System.IO.File.SetCreationTimeUtc(FullName, value.Value); //FileInfo does not work on Linux according to https://stackoverflow.com/a/17126045/1582323
            else
                throw new InvalidOperationException("The file does not exist");
        }
    }

    public DateTime? LastWriteTimeUtc
    {
        get => Exists ? System.IO.File.GetLastWriteTimeUtc(FullName) : null;
        set
        {
            if (Exists)
                System.IO.File.SetLastWriteTimeUtc(FullName, value.Value); //FileInfo does not work on Linux according to https://stackoverflow.com/a/17126045/1582323
            else
                throw new InvalidOperationException("The file does not exist");
        }
    }

    public long Length => new FileInfo(FullName).Length;
    //public bool IsIgnoreFile { get; }
    //public IEnumerable<File> GetFiles();
    //public IEnumerable<File> GetDirectories();

    public bool IsPointerFile => fullName.EndsWith(PointerFile.Extension, StringComparison.OrdinalIgnoreCase);
    public bool IsBinaryFile  => !IsPointerFile;

    public string BinaryFileFullName => fullName.RemoveSuffix(PointerFile.Extension, StringComparison.OrdinalIgnoreCase);
    //public string BinaryFileFullNamePlatformNeutral => BinaryFileFullName.ToPlatformNeutralPath();

    public string PointerFileFullName => IsPointerFile ? FullName : FullName + PointerFile.Extension;
    //public string PointerFileFullNamePlatformNeutral => PointerFileFullName.ToPlatformNeutralPath();

    public PointerFile GetPointerFile(DirectoryInfo root)
    {
        if (IsPointerFile)
            // this File is a PointerFile, return it as is
            return PointerFile.FromFullName(root, fullName);
        else
            // this File is a BinaryFile, return the equivalent PointerFile
            return PointerFile.FromFullName(root, PointerFileFullName);
    }

    public BinaryFile  GetBinaryFile(DirectoryInfo root)
    {
        if (IsBinaryFile)
            // this File is a BinaryFile, return as is
            return BinaryFile.FromFullName(root, fullName);
        else
            // this File is a PointerFile, return the equivalent BinaryFile
            return BinaryFile.FromFullName(root, BinaryFileFullName);
    }

    public Stream OpenRead() => new FileStream(
        fullName,
        FileMode.Open,
        FileAccess.Read,
        FileShare.Read,
        bufferSize: 32768, // 32 KB buffer size
        useAsync: true); // Enable async I/O for better performance with large files

    public Stream OpenWrite() => new FileStream(
        fullName,
        FileMode.OpenOrCreate,
        FileAccess.Write,
        FileShare.None,
        bufferSize: 32768, // 32 KB buffer size
        useAsync: true); // Enable async I/O for better performance with large files

    public void Delete()
    {
        var newFullName = System.IO.Path.Combine(Path!, destinationName);
        System.IO.File.Copy(FullName, newFullName);
        return FromFullName(newFullName);
    }

    public void Delete() => System.IO.File.Delete(fullName);

    public virtual bool Equals(File? other)
    {
        return other is not null &&
               string.Equals(this.FullName, other.FullName, StringComparison.OrdinalIgnoreCase);
    }

    public override int GetHashCode() => FullName.GetHashCode();

    public override string ToString() => FullName;
}

public record StateDatabaseFile : File
{
    public static readonly string Extension     = ".ariusdb";
    public static readonly string TempExtension = ".ariusdb.tmp";

    public RepositoryVersion Version { get; }

    private StateDatabaseFile(string fullName, RepositoryVersion version) : base(fullName)
    {
        if (!fullName.EndsWith(Extension, StringComparison.OrdinalIgnoreCase) && 
            !fullName.EndsWith(TempExtension, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"'{fullName}' is not a valid StateDatabaseFile");

        this.Version = version;
        this.IsTemp  = fullName.EndsWith(TempExtension, StringComparison.OrdinalIgnoreCase);
    }

    public static StateDatabaseFile FromRepositoryVersion(DirectoryInfo stateDbFolder, RepositoryVersion version, bool isTemp) => new(System.IO.Path.Combine(stateDbFolder.FullName, GetFileSystemName(version, isTemp)), version);

    public static StateDatabaseFile FromFullName(DirectoryInfo stateDbFolder, string fullName)
    {
        var version = GetVersion(fullName);
        return new(fullName, version);
    }
    public bool IsTemp { get; }

    public StateDatabaseFile GetTempCopy()
    {
        if (IsTemp)
            throw new InvalidOperationException("This is already a temp file");

        var f = base.CopyTo(GetFileSystemName(Version, true));

        return new(f.FullName, Version);
    }

    private static string GetFileSystemName(RepositoryVersion version, bool temp)
    {
        return $"{version.Name.Replace(":", "")}{(temp ? TempExtension : Extension)}";
    }

    private static RepositoryVersion GetVersion(string name)
    {
        var n = System.IO.Path.GetFileName(name).RemoveSuffix(TempExtension).RemoveSuffix(Extension);
        if (DateTime.TryParseExact(n, "yyyy-MM-ddTHHmmss", null, System.Globalization.DateTimeStyles.AssumeUniversal, out var parsedDateTime))
        {
            return parsedDateTime;
        }
        else
        {
            return new RepositoryVersion { Name = n };
        }

    }
}

public interface IRelativeFile : IFile
{
    public DirectoryInfo Root { get; }

    public string RelativeName { get; }

    // TODO other properties
}

public abstract record RelativeFile : File, IRelativeFile
{

    //protected RelativeFile(DirectoryInfo root, FileInfo fileInfo) : base(fileInfo)
    //{
    //    Root = root;
    //}

    protected RelativeFile(DirectoryInfo root, string fullName) : base(fullName)
    {
        Root = root;
    }

    public DirectoryInfo Root                        { get; }
    public string        RelativeName                => base.GetRelativeName(Root);
    public string        RelativeNamePlatformNeutral => base.GetRelativeNamePlatformNeutral(Root);

    //public PointerFile GetPointerFile() => base.GetPointerFile(Root);
    //public BinaryFile  GetBinaryFile()  => base.GetBinaryFile(Root);

    public virtual bool Equals(RelativeFile? other)
    {
        return other is not null 
               && base.Equals((File)other)
               && string.Equals(this.Root?.FullName, other.Root?.FullName, StringComparison.OrdinalIgnoreCase);
    }

    public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), Root?.FullName);

    public override string ToString() => RelativeName;
}

public record PointerFile : RelativeFile
{
    public static readonly string        Extension = ".pointer.arius";

    //protected PointerFile(DirectoryInfo root, FileInfo fileInfo) : base(root, fileInfo)
    //{
    //}

    protected PointerFile(DirectoryInfo root, string fullName) : base(root, fullName)
    {
        if (!fullName.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"'{fullName}' is not a valid PointerFile");
    }

    //public static PointerFile FromFileInfo(DirectoryInfo root, FileInfo fi)             => new(root, fi);
    public static PointerFile FromFullName(DirectoryInfo root, string fullName)         => new(root, fullName);
    public static PointerFile FromRelativeName(DirectoryInfo root, string relativeName) => new(root, System.IO.Path.Combine(root.FullName, relativeName));


    public override string ToString() => RelativeName;
}

public interface IFileWithHash : IFile
{
    public Hash Hash { get; }
}

public record PointerFileWithHash : PointerFile, IFileWithHash
{
    //protected PointerFileWithHash(DirectoryInfo root, FileInfo fileInfo, Hash hash) : base(root, fileInfo)
    //{
    //    Hash = hash;
    //}
    protected PointerFileWithHash(DirectoryInfo root, string fullName, Hash hash) : base(root, fullName)
    {
        Hash = hash;
    }

    //public static PointerFileWithHash FromFileInfo(DirectoryInfo root, FileInfo fi, Hash h)             => new(root, fi, h);
    public static PointerFileWithHash FromFullName(DirectoryInfo root, string fullName, Hash h)         => new(root, fullName, h);
    public static PointerFileWithHash FromRelativeName(DirectoryInfo root, string relativeName, Hash h) => new(root, System.IO.Path.Combine(root.FullName, relativeName), h);
    public static PointerFileWithHash FromBinaryFileWithHash(BinaryFileWithHash bfwh)                   => new(bfwh.Root, bfwh.FullName, bfwh.Hash);
    
    /// <summary>
    /// Get a PointerFile with Hash by reading the value in the PointerFile
    /// </summary>
    /// <returns></returns>
    public static PointerFileWithHash FromExistingPointerFile(PointerFile pf)
    {
        if (!System.IO.File.Exists(pf.FullName))
            throw new ArgumentException($"'{pf.FullName}' does not exist");

        try
        {
            var json = System.IO.File.ReadAllBytes(pf.FullName);
            var pfc  = JsonSerializer.Deserialize<PointerFileContents>(json);
            var h    = new Hash(pfc.BinaryHash.HexStringToBytes());

            return PointerFileWithHash.FromFullName(pf.Root, pf.FullName, h);
        }
        catch (JsonException e)
        {
            throw new ArgumentException($"'{pf.FullName}' is not a valid PointerFile", e);
        }
    }
    /// <summary>
    /// Write the PointerFile to disk
    /// </summary>
    public static PointerFileWithHash Create(BinaryFileWithHash bfwh) => Create(bfwh.Root, bfwh.RelativeName + PointerFile.Extension, bfwh.Hash, bfwh.CreationTimeUtc.Value, bfwh.LastWriteTimeUtc.Value);
    public static PointerFileWithHash Create(DirectoryInfo root, PointerFileEntry pfe) => Create(root, pfe.RelativeName + PointerFile.Extension, pfe.Hash, pfe.CreationTimeUtc, pfe.LastWriteTimeUtc);
    public static PointerFileWithHash Create(DirectoryInfo root, string relativeName, Hash hash, DateTime creationTimeUtc, DateTime lastWriteTimeUtc)
    {
        var pfwh = FromRelativeName(root, relativeName, hash);

        Directory.CreateDirectory(pfwh.Path);

        var pfc = new PointerFileContents(hash.Value.BytesToHexString());
        var json = JsonSerializer.SerializeToUtf8Bytes(pfc); //ToUtf8 is faster https://docs.microsoft.com/en-us/dotnet/standard/serialization/system-text-json-how-to?pivots=dotnet-6-0#serialize-to-utf-8
        System.IO.File.WriteAllBytes(pfwh.FullName, json);

        pfwh.CreationTimeUtc  = creationTimeUtc;
        pfwh.LastWriteTimeUtc = lastWriteTimeUtc;

        return pfwh;
    }
    private record PointerFileContents(string BinaryHash);


    public Hash Hash { get; }

    public override string ToString() => RelativeName;
}



public record BinaryFile : RelativeFile
{
    //protected BinaryFile(DirectoryInfo root, FileInfo fileInfo) : base(root, fileInfo)
    //{
    //}

    protected BinaryFile(DirectoryInfo root, string fullName) : base(root, fullName)
    {
        if (fullName.EndsWith(PointerFile.Extension, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"'{fullName}' is a PointerFile not a BinaryFile");
    }

    //public static BinaryFile FromFileInfo(DirectoryInfo root, FileInfo fi)             => new(root, fi);
    public static BinaryFile FromFullName(DirectoryInfo root, string fullName)         => new(root, fullName);
    public static BinaryFile FromRelativeName(DirectoryInfo root, string relativeName) => new(root, System.IO.Path.Combine(root.FullName, relativeName));
    

    public override string ToString() => RelativeName;
}

public interface IBinaryFileWithHash : IFileWithHash
{
}

public record BinaryFileWithHash : BinaryFile, IBinaryFileWithHash // to private
{
    //protected BinaryFileWithHash(DirectoryInfo root, FileInfo fileInfo, Hash hash) : base(root, fileInfo)
    //{
    //    Hash = hash;
    //}

    protected BinaryFileWithHash(DirectoryInfo root, string fullName, Hash hash) : base(root, fullName)
    {
        Hash = hash;
    }

    //public static BinaryFileWithHash FromFileInfo(DirectoryInfo root, FileInfo fi, Hash h)             => new(root, fi, h);
    public static BinaryFileWithHash FromFullName(DirectoryInfo root, string fullName, Hash h)         => new(root, fullName, h);
    public static BinaryFileWithHash FromRelativeName(DirectoryInfo root, string relativeName, Hash h) => new(root, System.IO.Path.Combine(root.FullName, relativeName), h);
    public static BinaryFileWithHash FromBinaryFile(BinaryFile bf, Hash h)                             => new(bf.Root, bf.FullName, h);

    public Hash Hash { get; }

    //public PointerFileWithHash GetPointerFileWithHash() => PointerFileWithHash.FromFullName(root, FullName + PointerFile.Extension, Hash);
    public PointerFileEntry    GetPointerFileEntry()    => PointerFileEntry.FromBinaryFileWithHash(this);


    public override string           ToString() => RelativeName;
}

public enum FilePairType
{
    PointerFileOnly,
    BinaryFileOnly,
    BinaryFileWithPointerFile,
    None
}

public record FilePair
{
    public FilePair(PointerFile? pointerFile, BinaryFile? binaryFile)
    {
        PointerFile = pointerFile;
        BinaryFile  = binaryFile;
    }

    
    public PointerFile? PointerFile { get; }
    public BinaryFile?  BinaryFile  { get;}

    public string RelativeName => BinaryFile?.RelativeName ?? PointerFile!.RelativeName;

    public FilePairType Type
    {
        get
        {
            if (PointerFile is not null && BinaryFile is not null)
                return FilePairType.BinaryFileWithPointerFile;
            else if (PointerFile is not null && BinaryFile is null)
                return FilePairType.PointerFileOnly;
            else if (PointerFile is null && BinaryFile is not null)
                return FilePairType.BinaryFileOnly;
            else if (PointerFile is null && BinaryFile is null)
                return FilePairType.None;

            throw new InvalidOperationException();
        }
    }

    public bool IsBinaryFileWithPointerFile => PointerFile is not null && BinaryFile is not null;
    public bool IsPointerFileOnly           => PointerFile is not null && BinaryFile is null;
    public bool IsBinaryFileOnly            => PointerFile is null && BinaryFile is not null;

    public bool HasPointerFile => PointerFile is not null;
    public bool HasBinaryFile  => BinaryFile is not null;

    public bool HasExistingPointerFile => PointerFile is not null && PointerFile.Exists;
    public bool HasExistingBinaryFile  => BinaryFile is not null && BinaryFile.Exists;

    public override string ToString()
    {
        if (PointerFile is not null && BinaryFile is not null)
        {
            return $"FilePair PF+BF '{RelativeName}'";
        }
        else if (PointerFile is null && BinaryFile is not null)
        {
            return $"FilePair BF '{RelativeName}'";
        }
        else if (PointerFile is not null && BinaryFile is null)
        {
            return $"FilePair PF '{RelativeName}'";
        }
        else
            throw new InvalidOperationException("PointerFile and BinaryFile are both null");
    }
}

public record FilePairWithHash : FilePair
{
    public FilePairWithHash(PointerFileWithHash? pointerFile, BinaryFileWithHash? binaryFile) : base(pointerFile, binaryFile)
    {
        this.PointerFile = pointerFile;
        this.BinaryFile  = binaryFile;
    }
    
    public new PointerFileWithHash? PointerFile { get; }
    public new BinaryFileWithHash?  BinaryFile  { get; }

    public Hash Hash => BinaryFile?.Hash ?? PointerFile!.Hash;

    public override string ToString()
    {
        if (PointerFile is not null && BinaryFile is not null)
        {
            return $"FilePairWithHash PF+BF '{RelativeName}' ({PointerFile.Hash.ToShortString()})";
        }
        else if (PointerFile is null && BinaryFile is not null)
        {
            return $"FilePairWithHash BF '{RelativeName}' ({BinaryFile.Hash.ToShortString()})";
        }
        else if (PointerFile is not null && BinaryFile is null)
        {
            return $"FilePairWithHash PF '{RelativeName}' ({PointerFile.Hash.ToShortString()})";
        }
        else
            throw new InvalidOperationException("PointerFile and BinaryFile are both null");
    }
}