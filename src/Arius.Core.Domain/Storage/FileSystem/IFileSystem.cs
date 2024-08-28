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
    public string  GetRelativePath(string relativeTo)        => System.IO.Path.GetRelativePath(relativeTo, fileInfo.FullName);
    public string  GetRelativePathPlatformNeutral(string relativeTo)        => GetRelativePath(relativeTo).ToPlatformNeutralPath();
    public string  GetRelativePath(DirectoryInfo relativeTo) => GetRelativePath(relativeTo.FullName);
    public string  GetRelativePathPlatformNeutral(DirectoryInfo relativeTo) => GetRelativePathPlatformNeutral(relativeTo.FullName);

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

    public PointerFile GetPointerFile()
    {
        if (IsPointerFile)
            // this File is a PointerFile, return it as is
            return new PointerFile(fileInfo);
        else
            // this File is a BinaryFile, return the equivalent PointerFile
            return new PointerFile(PointerFileFullName);
    }

    public BinaryFile  GetBinaryFile()
    {
        if (IsBinaryFile)
            // this File is a BinaryFile, return as is
            return new BinaryFile(fileInfo);
        else
            // this File is a PointerFile, return the equivalent BinaryFile
            return new BinaryFile(BinaryFileFullName);
    }

    public override string ToString() => FullName;
}

public record PointerFile : File
{
    public static readonly string Extension = ".pointer.arius";

    public PointerFile(FileInfo fileInfo) : base(fileInfo)
    {
    }

    public PointerFile(string fullName) : base(fullName)
    {
    }
}

public record BinaryFile : File
{
    public BinaryFile(FileInfo fileInfo) : base(fileInfo)
    {
    }
    public BinaryFile(string fullName) : base(fullName)
    {
    }
}

internal static class FileExtensions
{
    public static string GetBinaryFileFullName(this File file)
    {
        return file.FullName.RemoveSuffix(PointerFile.Extension, StringComparison.OrdinalIgnoreCase);
    }
    
}