using System.Text.Json;
using Arius.Core.Domain.Extensions;
using Arius.Core.Domain.Services;

namespace Arius.Core.Domain.Storage.FileSystem;

public interface IFileSystem
{
    public IEnumerable<File> EnumerateFiles(DirectoryInfo directory);
}

public record File
{
    protected readonly FileInfo fileInfo;

    public File(FileInfo fileInfo)
    {
        this.fileInfo = fileInfo;
    }
    public File(string fullName) 
        : this(new FileInfo(fullName))
    {
    }

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

    public DateTime CreationTimeUtc
    {
        get => System.IO.File.GetCreationTimeUtc(FullName);
        set => System.IO.File.SetCreationTimeUtc(FullName, value); //FileInfo does not work on Linux according to https://stackoverflow.com/a/17126045/1582323
    }

    public DateTime LastWriteTimeUtc
    {
        get => System.IO.File.GetLastWriteTimeUtc(FullName);
        set => System.IO.File.SetLastWriteTimeUtc(FullName, value); //FileInfo does not work on Linux according to https://stackoverflow.com/a/17126045/1582323
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
            return new PointerFile(root, fileInfo);
        else
            // this File is a BinaryFile, return the equivalent PointerFile
            return new PointerFile(root, PointerFileFullName);
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

    public override string ToString() => FullName;
}

public abstract record RelativeFile : File
{
    protected readonly DirectoryInfo root;

    public RelativeFile(DirectoryInfo root, FileInfo fileInfo) : base(fileInfo)
    {
        this.root = root;
    }

    public RelativeFile(DirectoryInfo root, string fullName) : base(fullName)
    {
        this.root = root;
    }

    public string RelativeName                => base.GetRelativeName(root);
    public string RelativeNamePlatformNeutral => base.GetRelativeNamePlatformNeutral(root);

    public PointerFile GetPointerFile() => base.GetPointerFile(root);
    public BinaryFile  GetBinaryFile()  => base.GetBinaryFile(root);

    public override string ToString() => RelativeName;
}

public record PointerFile : RelativeFile
{
    public static readonly string        Extension = ".pointer.arius";

    public PointerFile(DirectoryInfo root, FileInfo fileInfo) : base(root, fileInfo)
    {
    }

    public PointerFile(DirectoryInfo root, string fullName) : base(root, fullName)
    {
    }

    /// <summary>
    /// Get a PointerFile with Hash by reading the value in the PointerFile
    /// </summary>
    /// <returns></returns>
    public PointerFileWithHash GetPointerFileWithHash()
    {
        var h = ReadPointerFile(FullName);
        return new PointerFileWithHash(root, fileInfo, h);
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
    protected static void WritePointerFile(PointerFileWithHash pfwh)
    {
        Directory.CreateDirectory(pfwh.Path);

        var pfc = new PointerFileContents(pfwh.Hash.Value.BytesToHexString());
        var json = JsonSerializer.SerializeToUtf8Bytes(pfc); //ToUtf8 is faster https://docs.microsoft.com/en-us/dotnet/standard/serialization/system-text-json-how-to?pivots=dotnet-6-0#serialize-to-utf-8
        System.IO.File.WriteAllBytes(pfwh.FullName, json);

        //pfwh.CreationTimeUtc  = creationTimeUtc;
        //pfwh.LastWriteTimeUtc = lastWriteTimeUtc;
    }
    private record PointerFileContents(string BinaryHash);

    public override string ToString() => RelativeName;
}

public record PointerFileWithHash : PointerFile
{
    private readonly Hash hash;

    public PointerFileWithHash(DirectoryInfo root, FileInfo fileInfo, Hash hash) : base(root, fileInfo)
    {
        Hash = hash;
    }

    public PointerFileWithHash(DirectoryInfo root, string fullName, Hash hash) : base(root, fullName)
    {
        Hash = hash;
    }

    /// <summary>
    /// Write the PointerFile to disk
    /// </summary>
    public void Save()
    {
        PointerFile.WritePointerFile(this);
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

    public static BinaryFile FromFileInfo(DirectoryInfo root, FileInfo fi) => new(root, fi);
    public static BinaryFile FromFullName(DirectoryInfo root, string fullName) => new(root, fullName);
    public static BinaryFile FromRelative(DirectoryInfo root, string relativeName) => new(root, System.IO.Path.Combine(root.FullName, relativeName));
    
    /// <summary>
    /// Get a BinaryFile with the provided Hash value
    /// </summary>
    /// <param name="h"></param>
    /// <returns></returns>
    public BinaryFileWithHash GetBinaryFileWithHash(Hash h) => new(root, fileInfo, h);

    public override string ToString() => RelativeName;
}

public record BinaryFileWithHash : BinaryFile
{
    public BinaryFileWithHash(DirectoryInfo root, FileInfo fileInfo, Hash hash) : base(root, fileInfo)
    {
        Hash = hash;
    }

    public BinaryFileWithHash(DirectoryInfo root, string fullName, Hash hash) : base(root, fullName)
    {
        Hash = hash;
    }

    public Hash Hash { get; }

    public PointerFileWithHash GetPointerFileWithHash() => new(root, fileInfo, Hash);

    public override string ToString() => RelativeName;
}