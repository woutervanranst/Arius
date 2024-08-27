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

    public string FullName { get; }
    public string Path     { get; }
    public string Name     { get; }

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

    public PointerFile GetPointerFile()
    {
        if (!IsPointerFile)
            throw new InvalidOperationException("This is not a PointerFile");

        return new PointerFile(fileInfo);
    }

    public BinaryFile  GetBinaryFile()
    {
        if (!IsBinaryFile)
            throw new InvalidOperationException("This is a PointerFile");

        return new BinaryFile(fileInfo);
    }
}

public record PointerFile : File
{
    public static readonly string Extension = ".pointer.arius";

    public PointerFile(FileInfo fileInfo) : base(fileInfo)
    {
    }
}

public record BinaryFile : File
{
    public BinaryFile(FileInfo fileInfo) : base(fileInfo)
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