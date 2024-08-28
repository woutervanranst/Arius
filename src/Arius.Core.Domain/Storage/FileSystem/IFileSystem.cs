using Arius.Core.Domain.Extensions;

namespace Arius.Core.Domain.Storage.FileSystem;

public interface IFileSystem
{
    public IEnumerable<File> EnumerateFiles(DirectoryInfo directory);
}

public record File
{
    private readonly FileInfo fileInfo;

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
            return new BinaryFile(root, fileInfo);
        else
            // this File is a PointerFile, return the equivalent BinaryFile
            return new BinaryFile(root, BinaryFileFullName);
    }

    public override string ToString() => FullName;
}

public abstract record RelativeFile : File
{
    private readonly DirectoryInfo root;

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

    public override string ToString() => RelativeName;
}



public record BinaryFile : RelativeFile
{
    public BinaryFile(DirectoryInfo root, FileInfo fileInfo) : base(root, fileInfo)
    {
    }

    public BinaryFile(DirectoryInfo root, string fullName) : base(root,fullName)
    {
    }

    public override string ToString() => RelativeName;
}

internal static class FileExtensions
{
    public static string GetBinaryFileFullName(this File file)
    {
        return file.FullName.RemoveSuffix(PointerFile.Extension, StringComparison.OrdinalIgnoreCase);
    }
    
}