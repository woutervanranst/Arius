using Arius.Core.Extensions;
using Arius.Core.Repositories;
using System.Text.Json;
using Zio;

namespace Arius.Core.Models;

public enum FilePairType
{
    PointerFileOnly,
    BinaryFileOnly,
    BinaryFileWithPointerFile,
    None
}

public class FilePair : FileEntry
{
    public static FilePair FromBinaryFileFileEntry(FileEntry fe)                            => new(fe.FileSystem, fe.Path);
    public static FilePair FromBinaryFilePath(IFileSystem fileSystem, UPath binaryFilePath) => new(fileSystem, binaryFilePath);

    internal static FilePair FromPointerFileEntry(IFileSystem fileSystem, PointerFileEntryDto pfe)
    {
        var pointerFilePath = (UPath)pfe.RelativeName;
        var binaryFilePath = pointerFilePath.GetBinaryFilePath();
        return FilePair.FromBinaryFilePath(fileSystem, binaryFilePath);
    }
    private FilePair(IFileSystem fileSystem, UPath binaryFilePath) : base(fileSystem, binaryFilePath)
    {
        BinaryFile = BinaryFile.FromFileEntry(this);
        PointerFile = PointerFile.FromPath(fileSystem, binaryFilePath.GetPointerFilePath());
    }

    public BinaryFile BinaryFile { get; }
    public PointerFile PointerFile { get; }

    public BinaryFile? ExistingBinaryFile => BinaryFile.Exists ? BinaryFile : null;
    public PointerFile? ExistingPointerFile => PointerFile.Exists ? PointerFile : null;

    /// <summary>
    /// Get the FilePair Type, considering the EXISTING files
    /// </summary>
    public FilePairType Type
    {
        get
        {
            if (PointerFile.Exists && BinaryFile.Exists)
                return FilePairType.BinaryFileWithPointerFile;
            else if (PointerFile.Exists && !BinaryFile.Exists)
                return FilePairType.PointerFileOnly;
            else if (!PointerFile.Exists && BinaryFile.Exists)
                return FilePairType.BinaryFileOnly;
            else if (!PointerFile.Exists && !BinaryFile.Exists)
                return FilePairType.None;
            else
                throw new InvalidOperationException();
        }
    }

    public long? Length => ExistingBinaryFile?.Length;

    public PointerFile GetOrCreatePointerFile(Hash h)
    {
        if (Type == FilePairType.PointerFileOnly)
            return PointerFile;

        var pf = BinaryFile.GetPointerFile();

        pf.Write(h, BinaryFile.CreationTime, BinaryFile.LastWriteTime);

        return pf;
    }

    public override string ToString() =>
        Type switch
        {
            FilePairType.PointerFileOnly           => $"FilePair PF '{FullName}'",
            FilePairType.BinaryFileOnly            => $"FilePair BF '{FullName}'",
            FilePairType.BinaryFileWithPointerFile => $"FilePair PF+BF '{FullName}'",
            _                                      => throw new InvalidOperationException("PointerFile and BinaryFile are both null")
        };
}

public class BinaryFile : FileEntry
{
    public static BinaryFile FromFileEntry(FileEntry fe) => new(fe.FileSystem, fe.Path);

    private BinaryFile(IFileSystem fileSystem, UPath path) : base(fileSystem, path)
    {
        if (path.IsPointerFilePath())
            throw new ArgumentException("This is a PointerFile path", nameof(path));
    }

    private static readonly FileStreamOptions smallFileStreamReadOptions = new() { Mode = FileMode.Open, Access = FileAccess.Read, Share = FileShare.Read, BufferSize = 1024 };
    private static readonly FileStreamOptions largeFileStreamReadOptions = new() { Mode = FileMode.Open, Access = FileAccess.Read, Share = FileShare.Read, BufferSize = 32768, Options = FileOptions.Asynchronous };
    public Stream OpenRead() => File.Open(this.ConvertPathToInternal(), Length <= 1024 ? smallFileStreamReadOptions : largeFileStreamReadOptions);

    //    private static readonly SIO.FileStreamOptions smallFileStreamWriteOptions = new() { Mode = SIO.FileMode.OpenOrCreate, Access = SIO.FileAccess.Write, Share = SIO.FileShare.None, BufferSize = 1024 };
    //    private static readonly SIO.FileStreamOptions largeFileStreamWriteOptions = new() { Mode = SIO.FileMode.OpenOrCreate, Access = SIO.FileAccess.Write, Share = SIO.FileShare.None, BufferSize = 32768, Options = SIO.FileOptions.Asynchronous };
    //    public SIO.Stream OpenWrite() => _fileSystem.File.Open(_fullNamePath, Length <= 1024 ? smallFileStreamWriteOptions : largeFileStreamWriteOptions);
    
    // TODO optimize
    public Stream OpenWrite() => File.Open(this.ConvertPathToInternal(), FileMode.CreateNew);

    public PointerFile GetPointerFile()
    {
        var fe = new FileEntry(FileSystem, Path.GetPointerFilePath());
        return PointerFile.FromFileEntry(fe);
    }
}

public class PointerFile : FileEntry
{
    public static readonly string Extension = ".pointer.arius";

    public static PointerFile FromFileEntry(FileEntry fe) => new(fe.FileSystem, fe.Path);
    public static PointerFile FromPath(IFileSystem fileSystem, UPath pointerFilePath) => new(fileSystem, pointerFilePath);

    private PointerFile(IFileSystem fileSystem, UPath path) : base(fileSystem, path)
    {
        if (!path.IsPointerFilePath())
            throw new ArgumentException("This is not a PointerFile path", nameof(path));
    }

    public BinaryFile GetBinaryFile()
    {
        var fe = new FileEntry(FileSystem, Path.GetBinaryFilePath());

        return BinaryFile.FromFileEntry(fe);
    }

    public Hash ReadHash()
    {
        var json = ReadAllBytes(); // throws a FileNotFoundException if not exists
        var pfc = JsonSerializer.Deserialize<PointerFileContents>(json);

        return pfc!.BinaryHash;
    }

    public void Write(Hash h, DateTime creationTime, DateTime lastWriteTime)
    {
        var pfc = new PointerFileContents(h.ToString());

        var json = JsonSerializer.SerializeToUtf8Bytes(pfc);
        WriteAllBytes(json);

        CreationTime = creationTime;
        LastWriteTime = lastWriteTime;
    }
    private record PointerFileContents(string BinaryHash);
}