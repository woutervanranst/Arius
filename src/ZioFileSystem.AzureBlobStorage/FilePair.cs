using SIO = System.IO;
using System;
using Zio;

namespace ZioFileSystem.AzureBlobStorage;

public record FilePair(PointerFile? PointerFile, BinaryFile? BinaryFile);

public class BinaryFile : FileEntry
{
    public static BinaryFile FromFileEntry(FileEntry fe) => new(fe.FileSystem, fe.Path);

    private BinaryFile(IFileSystem fileSystem, UPath path) : base(fileSystem, path)
    {
        if (path.FullName.EndsWith(PointerFile.Extension, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Path cannot end with PointerFile.Extension", nameof(path));
    }

    private static readonly SIO.FileStreamOptions smallFileStreamReadOptions = new() { Mode = SIO.FileMode.Open, Access = SIO.FileAccess.Read, Share = SIO.FileShare.Read, BufferSize = 1024 };
    private static readonly SIO.FileStreamOptions largeFileStreamReadOptions = new() { Mode = SIO.FileMode.Open, Access = SIO.FileAccess.Read, Share = SIO.FileShare.Read, BufferSize = 32768, Options = SIO.FileOptions.Asynchronous };
    public SIO.Stream OpenRead() => SIO.File.Open(this.ConvertPathToInternal(), Length <= 1024 ? smallFileStreamReadOptions : largeFileStreamReadOptions);

    //    private static readonly SIO.FileStreamOptions smallFileStreamWriteOptions = new() { Mode = SIO.FileMode.OpenOrCreate, Access = SIO.FileAccess.Write, Share = SIO.FileShare.None, BufferSize = 1024 };
    //    private static readonly SIO.FileStreamOptions largeFileStreamWriteOptions = new() { Mode = SIO.FileMode.OpenOrCreate, Access = SIO.FileAccess.Write, Share = SIO.FileShare.None, BufferSize = 32768, Options = SIO.FileOptions.Asynchronous };
    //    public SIO.Stream OpenWrite() => _fileSystem.File.Open(_fullNamePath, Length <= 1024 ? smallFileStreamWriteOptions : largeFileStreamWriteOptions);

    public PointerFile GetPointerFile()
    {
        var pfPath = Path.ChangeExtension($"{ExtensionWithDot}{PointerFile.Extension}");
        var fe = new FileEntry(this.FileSystem, pfPath);
        return PointerFile.FromFileEntry(fe);
    }
}

public class PointerFile : FileEntry
{
    public static readonly string Extension = ".pointer.arius";

    public static PointerFile FromFileEntry(FileEntry fe) => new(fe.FileSystem, fe.Path);

    private PointerFile(IFileSystem fileSystem, UPath path) : base(fileSystem, path)
    {
    }

    public BinaryFile GetBinaryFile()
    {
        var bfPath = Path.RemoveSuffix(PointerFile.Extension);
        var fe = new FileEntry(this.FileSystem, bfPath);

        return BinaryFile.FromFileEntry(fe);
    }
}