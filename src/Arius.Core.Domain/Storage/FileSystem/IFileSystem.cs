using Arius.Core.Domain.Extensions;
using System.Text.Json;

namespace Arius.Core.Domain.Storage.FileSystem;

public interface IFileSystem
{
    public IEnumerable<File> EnumerateFiles(DirectoryInfo directory);
}

public interface IFile
{
    string  FullName                { get; }
    string  FullNamePlatformNeutral { get; }
    string? Path                    { get; }
    string? PathPlatformNeutral     { get; }
    string  Name                    { get; }
    bool    Exists                  { get; }

    DateTime? CreationTimeUtc { get; set; }

    DateTime? LastWriteTimeUtc { get; set; }

    bool   IsPointerFile                      { get; }
    bool   IsBinaryFile                       { get; }
    string BinaryFileFullName                 { get; }
    string BinaryFileFullNamePlatformNeutral  { get; }
    string PointerFileFullName                { get; }
    string PointerFileFullNamePlatformNeutral { get; }

    string      GetRelativeName(string relativeTo);
    string      GetRelativeNamePlatformNeutral(string relativeTo);
    string      GetRelativeName(DirectoryInfo relativeTo);
    string      GetRelativeNamePlatformNeutral(DirectoryInfo relativeTo);
    PointerFile GetPointerFile(DirectoryInfo root);
    BinaryFile  GetBinaryFile(DirectoryInfo root);

    Stream OpenRead();
    Stream OpenWrite();
}

public record File : IFile // TODO make internal
{
    protected readonly FileInfo fileInfo;

    protected File(FileInfo fileInfo)
    {
        this.fileInfo = fileInfo;
    }
    protected File(string fullName) : this(new FileInfo(fullName))
    {
    }

    public static File FromFileInfo(FileInfo fi)             => new(fi);
    public static File FromFullName(string fullName)         => new(fullName);
    public static File FromRelativeName(DirectoryInfo root, string relativeName) => new(System.IO.Path.Combine(root.FullName, relativeName));

    public string  FullName                                  => fileInfo.FullName;
    public string  FullNamePlatformNeutral                   => FullName.ToPlatformNeutralPath();
    public string? Path                                      => fileInfo.DirectoryName;
    public string? PathPlatformNeutral                       => Path?.ToPlatformNeutralPath();
    public string  Name                                      => fileInfo.Name;

    public string GetRelativeName(string relativeTo)                       => System.IO.Path.GetRelativePath(relativeTo, fileInfo.FullName);
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

    //public DateTime LastWriteTimeUtc
    //{
    //    get => System.IO.File.GetLastWriteTimeUtc(FullName);
    //    set => System.IO.File.SetLastWriteTimeUtc(FullName, value); //FileInfo does not work on Linux according to https://stackoverflow.com/a/17126045/1582323
    //}

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

    //public long Length { get; }
    //public DateTime LastWriteTimeUtc { get; }
    //public bool IsHiddenOrSystem { get; }
    //public bool IsIgnoreFile { get; }
    //public bool IsDirectory { get; }
    //public IEnumerable<File> GetFiles();
    //public IEnumerable<File> GetDirectories();

    public bool IsPointerFile => fileInfo.FullName.EndsWith(PointerFile.Extension, StringComparison.OrdinalIgnoreCase);
    public bool IsBinaryFile  => !IsPointerFile;

    public string BinaryFileFullName => fileInfo.FullName.RemoveSuffix(PointerFile.Extension, StringComparison.OrdinalIgnoreCase);
    public string BinaryFileFullNamePlatformNeutral => BinaryFileFullName.ToPlatformNeutralPath();

    public string PointerFileFullName => IsPointerFile ? FullName : FullName + PointerFile.Extension;
    public string PointerFileFullNamePlatformNeutral => PointerFileFullName.ToPlatformNeutralPath();

    public PointerFile GetPointerFile(DirectoryInfo root)
    {
        if (IsPointerFile)
            // this File is a PointerFile, return it as is
            return PointerFile.FromFileInfo(root, fileInfo);
        else
            // this File is a BinaryFile, return the equivalent PointerFile
            return PointerFile.FromFullName(root, PointerFileFullName);
    }

    public BinaryFile  GetBinaryFile(DirectoryInfo root)
    {
        if (IsBinaryFile)
            // this File is a BinaryFile, return as is
            return BinaryFile.FromFileInfo(root, fileInfo);
        else
            // this File is a PointerFile, return the equivalent BinaryFile
            return BinaryFile.FromFullName(root, BinaryFileFullName);
    }

    public Stream OpenRead() => new FileStream(
        fileInfo.FullName,
        FileMode.Open,
        FileAccess.Read,
        FileShare.Read,
        bufferSize: 32768, // 32 KB buffer size
        useAsync: true); // Enable async I/O for better performance with large files

    public Stream OpenWrite() => new FileStream(
        fileInfo.FullName,
        FileMode.OpenOrCreate,
        FileAccess.Write,
        FileShare.None,
        bufferSize: 32768, // 32 KB buffer size
        useAsync: true); // Enable async I/O for better performance with large files

    public virtual bool Equals(File? other)
    {
        return other is not null &&
               string.Equals(this.FullName, other.FullName, StringComparison.OrdinalIgnoreCase);
    }

    public override int GetHashCode() => FullName.GetHashCode();

    public override string ToString() => FullName;
}

public interface IRelativeFile : IFile
{
    public DirectoryInfo Root { get; }

    public string RelativeName { get; }

    // TODO other properties
}

public abstract record RelativeFile : File, IRelativeFile
{

    protected RelativeFile(DirectoryInfo root, FileInfo fileInfo) : base(fileInfo)
    {
        Root = root;
    }

    protected RelativeFile(DirectoryInfo root, string fullName) : base(fullName)
    {
        Root = root;
    }

    public DirectoryInfo Root                        { get; }
    public string        RelativeName                => base.GetRelativeName(Root);
    public string        RelativeNamePlatformNeutral => base.GetRelativeNamePlatformNeutral(Root);

    public PointerFile GetPointerFile() => base.GetPointerFile(Root);
    public BinaryFile  GetBinaryFile()  => base.GetBinaryFile(Root);

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

    protected PointerFile(DirectoryInfo root, FileInfo fileInfo) : base(root, fileInfo)
    {
    }

    protected PointerFile(DirectoryInfo root, string fullName) : base(root, fullName)
    {
    }

    public static PointerFile FromFileInfo(DirectoryInfo root, FileInfo fi)             => new(root, fi);
    public static PointerFile FromFullName(DirectoryInfo root, string fullName)         => new(root, fullName);
    public static PointerFile FromRelativeName(DirectoryInfo root, string relativeName) => new(root, System.IO.Path.Combine(root.FullName, relativeName));

    /// <summary>
    /// Get a PointerFile with Hash by reading the value in the PointerFile
    /// </summary>
    /// <returns></returns>
    public PointerFileWithHash GetPointerFileWithHash()
    {
        var h = ReadPointerFile(FullName);
        return PointerFileWithHash.FromFileInfo(Root, fileInfo, h);
    }

    protected static Hash ReadPointerFile(string fullName)
    {
        if (!System.IO.File.Exists(fullName))
            throw new ArgumentException($"'{fullName}' does not exist");

        try
        {
            var json = System.IO.File.ReadAllBytes(fullName);
            var pfc  = JsonSerializer.Deserialize<PointerFileContents>(json);
            var h    = new Hash(pfc.BinaryHash.HexStringToBytes());

            return h;
        }
        catch (JsonException e)
        {
            throw new ArgumentException($"'{fullName}' is not a valid PointerFile", e);
        }
    }
    protected record PointerFileContents(string BinaryHash);

    public override string ToString() => RelativeName;
}

public interface IFileWithHash : IFile
{
    public Hash Hash { get; }
}

public record PointerFileWithHash : PointerFile, IFileWithHash
{
    protected PointerFileWithHash(DirectoryInfo root, FileInfo fileInfo, Hash hash) : base(root, fileInfo)
    {
        Hash = hash;
    }
    protected PointerFileWithHash(DirectoryInfo root, string fullName, Hash hash) : base(root, fullName)
    {
        Hash = hash;
    }

    public static PointerFileWithHash FromFileInfo(DirectoryInfo root, FileInfo fi, Hash h)             => new(root, fi, h);
    public static PointerFileWithHash FromFullName(DirectoryInfo root, string fullName, Hash h)         => new(root, fullName, h);
    public static PointerFileWithHash FromRelativeName(DirectoryInfo root, string relativeName, Hash h) => new(root, System.IO.Path.Combine(root.FullName, relativeName), h);

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

    public Hash Hash { get; }

    public override string ToString() => RelativeName;
}



public record BinaryFile : RelativeFile
{
    protected BinaryFile(DirectoryInfo root, FileInfo fileInfo) : base(root, fileInfo)
    {
    }

    protected BinaryFile(DirectoryInfo root, string fullName) : base(root, fullName)
    {
    }

    public static BinaryFile FromFileInfo(DirectoryInfo root, FileInfo fi)             => new(root, fi);
    public static BinaryFile FromFullName(DirectoryInfo root, string fullName)         => new(root, fullName);
    public static BinaryFile FromRelativeName(DirectoryInfo root, string relativeName) => new(root, System.IO.Path.Combine(root.FullName, relativeName));
    
    /// <summary>
    /// Get a BinaryFile with the provided Hash value
    /// </summary>
    /// <param name="h"></param>
    /// <returns></returns>
    public BinaryFileWithHash GetBinaryFileWithHash(Hash h) => BinaryFileWithHash.FromFileInfo(Root, fileInfo, h);

    public override string ToString() => RelativeName;
}

public interface IBinaryFileWithHash : IFileWithHash
{
}

public record BinaryFileWithHash : BinaryFile, IBinaryFileWithHash // to private
{
    protected BinaryFileWithHash(DirectoryInfo root, FileInfo fileInfo, Hash hash) : base(root, fileInfo)
    {
        Hash = hash;
    }

    protected BinaryFileWithHash(DirectoryInfo root, string fullName, Hash hash) : base(root, fullName)
    {
        Hash = hash;
    }

    public static BinaryFileWithHash FromFileInfo(DirectoryInfo root, FileInfo fi, Hash h)             => new(root, fi, h);
    public static BinaryFileWithHash FromFullName(DirectoryInfo root, string fullName, Hash h)         => new(root, fullName, h);
    public static BinaryFileWithHash FromRelativeName(DirectoryInfo root, string relativeName, Hash h) => new(root, System.IO.Path.Combine(root.FullName, relativeName), h);

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