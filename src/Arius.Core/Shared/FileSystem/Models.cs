using Arius.Core.Shared.Hashing;
using Arius.Core.Shared.StateRepositories;
using System.Diagnostics;
using System.Text.Json;
using Zio;

namespace Arius.Core.Shared.FileSystem;

internal enum FilePairType
{
    PointerFileOnly,
    BinaryFileOnly,
    BinaryFileWithPointerFile,
    None
}

internal class FileEntryWithUtc : FileEntry
{
    public FileEntryWithUtc(IFileSystem fileSystem, UPath path) : base(fileSystem, path)
    {
    }

    public DateTime CreationTimeUtc
    {
        get => CreationTime.ToUniversalTime();
        set => CreationTime = value.ToLocalTime();
    }

    public DateTime LastWriteTimeUtc
    {
        get => LastWriteTime.ToUniversalTime();
        set => LastWriteTime = value.ToLocalTime();
    }
}

[DebuggerDisplay("FilePair ({Type}) - {FullName}")]
internal class FilePair : FileEntryWithUtc
{
    public static FilePair FromBinaryFileFileEntry(FileEntry fe)                            => new(fe.FileSystem, fe.Path);
    public static FilePair FromBinaryFilePath(IFileSystem fileSystem, UPath binaryFilePath) => new(fileSystem, binaryFilePath);

    internal static FilePair FromPointerFileEntry(IFileSystem fileSystem, PointerFileEntry pfe)
    {
        var pointerFilePath = (UPath)pfe.RelativeName;
        var binaryFilePath = pointerFilePath.GetBinaryFilePath();
        return FromBinaryFilePath(fileSystem, binaryFilePath);
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

    public PointerFile CreatePointerFile(Hash h)
    {
        if (!BinaryFile.Exists)
            throw new InvalidOperationException("Cannot call this method if BinaryFile does not exist");

        var pf = BinaryFile.GetPointerFile();

        pf.Write(h, BinaryFile.CreationTimeUtc, BinaryFile.LastWriteTimeUtc);

        return pf;
    }

    public PointerFile CreatePointerFile(Hash h, DateTime creationTimeUtc, DateTime lastWriteTimeUtc)
    {
        var pf = BinaryFile.GetPointerFile();

        pf.Write(h, creationTimeUtc, lastWriteTimeUtc);
        
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

[DebuggerDisplay("BinaryFile - {FullName}")]
internal class BinaryFile : FileEntryWithUtc
{
    public static BinaryFile FromFileEntry(FileEntry fe) => new(fe.FileSystem, fe.Path);

    private BinaryFile(IFileSystem fileSystem, UPath path) : base(fileSystem, path)
    {
        if (path.IsPointerFilePath())
            throw new ArgumentException("This is a PointerFile path", nameof(path));
    }

    public PointerFile GetPointerFile()
    {
        var fe = new FileEntry(FileSystem, Path.GetPointerFilePath());
        return PointerFile.FromFileEntry(fe);
    }

    public Stream OpenRead()
    {
        // MemoryFileSystem is used for testing and does not support FileStreamOptions so we fallback to the classic OpenFile method
        if (FileSystem is FilePairFileSystem { IsInMemory: true })
        {
            return FileSystem.OpenFile(Path, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
        else
        {
            var options = Length switch
            {
                < 4 * 1024         => ReadOptions.BufferSize4KB,
                < 64 * 1024        => ReadOptions.BufferSize8KB,
                < 1 * 1024 * 1024  => ReadOptions.BufferSize32KB,
                < 10 * 1024 * 1024 => ReadOptions.BufferSize64KB,
                _                  => ReadOptions.BufferSize256KB
            };

            return File.Open(this.ConvertPathToInternal(), options);
        }
    }

    public Stream OpenWrite(long expectedLength)
    {
        // MemoryFileSystem is used for testing and does not support FileStreamOptions so we fallback to the classic OpenFile method
        if (FileSystem is FilePairFileSystem { IsInMemory: true })
        {
            return FileSystem.OpenFile(Path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
        }
        else
        {
            var options = expectedLength switch
            {
                < 4 * 1024         => WriteOptions.BufferSize4KB,
                < 64 * 1024        => WriteOptions.BufferSize8KB,
                < 1 * 1024 * 1024  => WriteOptions.BufferSize32KB,
                < 10 * 1024 * 1024 => WriteOptions.BufferSize64KB,
                _                  => WriteOptions.BufferSize256KB
            };
            return File.Open(this.ConvertPathToInternal(), options);
        }
    }

    // Pre-allocated FileStreamOptions instances to avoid allocations on every call
    private static class ReadOptions
    {
        internal static readonly FileStreamOptions BufferSize4KB   = new() { Mode = FileMode.Open, Access = FileAccess.Read, Share = FileShare.Read, BufferSize = 4096 };
        internal static readonly FileStreamOptions BufferSize8KB   = new() { Mode = FileMode.Open, Access = FileAccess.Read, Share = FileShare.Read, BufferSize = 8192 };
        internal static readonly FileStreamOptions BufferSize32KB  = new() { Mode = FileMode.Open, Access = FileAccess.Read, Share = FileShare.Read, BufferSize = 32768, Options  = FileOptions.SequentialScan };
        internal static readonly FileStreamOptions BufferSize64KB  = new() { Mode = FileMode.Open, Access = FileAccess.Read, Share = FileShare.Read, BufferSize = 65536, Options  = FileOptions.Asynchronous | FileOptions.SequentialScan };
        internal static readonly FileStreamOptions BufferSize256KB = new() { Mode = FileMode.Open, Access = FileAccess.Read, Share = FileShare.Read, BufferSize = 262144, Options = FileOptions.Asynchronous | FileOptions.SequentialScan };
    }

    private static class WriteOptions
    {
        internal static readonly FileStreamOptions BufferSize4KB   = new() { Mode = FileMode.OpenOrCreate, Access = FileAccess.Write, Share = FileShare.None, BufferSize = 4096 };
        internal static readonly FileStreamOptions BufferSize8KB   = new() { Mode = FileMode.OpenOrCreate, Access = FileAccess.Write, Share = FileShare.None, BufferSize = 8192 };
        internal static readonly FileStreamOptions BufferSize32KB  = new() { Mode = FileMode.OpenOrCreate, Access = FileAccess.Write, Share = FileShare.None, BufferSize = 32768, Options  = FileOptions.SequentialScan };
        internal static readonly FileStreamOptions BufferSize64KB  = new() { Mode = FileMode.OpenOrCreate, Access = FileAccess.Write, Share = FileShare.None, BufferSize = 65536, Options  = FileOptions.Asynchronous | FileOptions.SequentialScan };
        internal static readonly FileStreamOptions BufferSize256KB = new() { Mode = FileMode.OpenOrCreate, Access = FileAccess.Write, Share = FileShare.None, BufferSize = 262144, Options = FileOptions.Asynchronous | FileOptions.SequentialScan };
    }
}

[DebuggerDisplay("PointerFile - {FullName}")]
internal class PointerFile : FileEntryWithUtc
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

    public void Write(Hash h, DateTime creationTimeUtc, DateTime lastWriteTimeUtc)
    {
        var pfc = new PointerFileContents(h.ToString());

        var json = JsonSerializer.SerializeToUtf8Bytes(pfc);
        WriteAllBytes(json);

        CreationTimeUtc = creationTimeUtc;
        LastWriteTimeUtc = lastWriteTimeUtc;
    }

    private record PointerFileContents(string BinaryHash);
}
