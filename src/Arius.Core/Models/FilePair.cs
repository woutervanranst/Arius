using Arius.Core.Extensions;
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
    
    public PointerFile GetPointerFile()
    {
        var fe = new FileEntry(FileSystem, Path.GetPointerFilePath());
        return PointerFile.FromFileEntry(fe);
    }

    // Pre-allocated FileStreamOptions instances to avoid allocations on every call
      private static class ReadOptions
      {
          internal static readonly FileStreamOptions Size4KB = new() { Mode = FileMode.Open, Access = FileAccess.Read, Share = FileShare.Read, BufferSize = 4096 };
          internal static readonly FileStreamOptions Size8KB = new() { Mode = FileMode.Open, Access = FileAccess.Read, Share = FileShare.Read, BufferSize = 8192 };
          internal static readonly FileStreamOptions Size32KB = new() { Mode = FileMode.Open, Access = FileAccess.Read, Share = FileShare.Read, BufferSize = 32768, Options = FileOptions.SequentialScan
   };
          internal static readonly FileStreamOptions Size64KB = new() { Mode = FileMode.Open, Access = FileAccess.Read, Share = FileShare.Read, BufferSize = 65536, Options = FileOptions.Asynchronous |
   FileOptions.SequentialScan };
          internal static readonly FileStreamOptions Size256KB = new() { Mode = FileMode.Open, Access = FileAccess.Read, Share = FileShare.Read, BufferSize = 262144, Options = FileOptions.Asynchronous
   | FileOptions.SequentialScan };
      }

      private static class WriteOptions
      {
          internal static readonly FileStreamOptions Size4KB = new() { Mode = FileMode.OpenOrCreate, Access = FileAccess.Write, Share = FileShare.None, BufferSize = 4096 };
          internal static readonly FileStreamOptions Size8KB = new() { Mode = FileMode.OpenOrCreate, Access = FileAccess.Write, Share = FileShare.None, BufferSize = 8192 };
          internal static readonly FileStreamOptions Size32KB = new() { Mode = FileMode.OpenOrCreate, Access = FileAccess.Write, Share = FileShare.None, BufferSize = 32768, Options =
  FileOptions.SequentialScan };
          internal static readonly FileStreamOptions Size64KB = new() { Mode = FileMode.OpenOrCreate, Access = FileAccess.Write, Share = FileShare.None, BufferSize = 65536, Options =
  FileOptions.Asynchronous | FileOptions.SequentialScan };
          internal static readonly FileStreamOptions Size256KB = new() { Mode = FileMode.OpenOrCreate, Access = FileAccess.Write, Share = FileShare.None, BufferSize = 262144, Options =
  FileOptions.Asynchronous | FileOptions.SequentialScan };
      }

      public Stream OpenRead()
      {
          [MethodImpl(MethodImplOptions.AggressiveInlining)]
          static FileStreamOptions GetOptimalOptions(long fileLength)
          {
              const int KB = 1024;
              const int MB = 1024 * 1024;

              return fileLength switch
              {
                  < 4 * KB => ReadOptions.Size4KB,
                  < 64 * KB => ReadOptions.Size8KB,
                  < 1 * MB => ReadOptions.Size32KB,
                  < 10 * MB => ReadOptions.Size64KB,
                  _ => ReadOptions.Size256KB
              };
          }

          if (FileSystem is MemoryFileSystem)
          {
              return FileSystem.OpenFile(Path, FileMode.Open, FileAccess.Read, FileShare.Read);
          }
          else
          {
              return File.Open(this.ConvertPathToInternal(), GetOptimalOptions(Length));
          }
      }

      public Stream OpenWrite()
      {
          [MethodImpl(MethodImplOptions.AggressiveInlining)]
          static FileStreamOptions GetOptimalOptions(long fileLength)
          {
              const int KB = 1024;
              const int MB = 1024 * 1024;

              return fileLength switch
              {
                  < 4 * KB => WriteOptions.Size4KB,
                  < 64 * KB => WriteOptions.Size8KB,
                  < 1 * MB => WriteOptions.Size32KB,
                  < 10 * MB => WriteOptions.Size64KB,
                  _ => WriteOptions.Size256KB
              };
          }

          if (FileSystem is MemoryFileSystem)
          {
              return FileSystem.OpenFile(Path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
          }
          else
          {
              return File.Open(this.ConvertPathToInternal(), GetOptimalOptions(Length));
          }
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
