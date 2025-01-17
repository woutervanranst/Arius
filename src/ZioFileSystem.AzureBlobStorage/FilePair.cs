using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Zio;
using Zio.FileSystems;

namespace ZioFileSystem.AzureBlobStorage;

public enum FilePairType
{
    PointerFileOnly,
    BinaryFileOnly,
    BinaryFileWithPointerFile,
    None
}

public class FilePair : FileEntry
{
    public static FilePair FromFileEntry(FileEntry fe) => new(fe.FileSystem, fe.Path);
    private FilePair(IFileSystem fileSystem, UPath binaryFilePath) : base(fileSystem, binaryFilePath)
    {
        BinaryFile = BinaryFile.FromFileEntry(this);
        PointerFile = PointerFile.FromPath(fileSystem, binaryFilePath.GetPointerFilePath());
    }

    public BinaryFile BinaryFile { get; }
    public PointerFile PointerFile { get; }

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

            throw new InvalidOperationException();
        }
    }

    public PointerFile GetOrCreatePointerFile(Hash h)
    {
        if (Type == FilePairType.PointerFileOnly)
            return PointerFile;

        var pf = BinaryFile.GetPointerFile();

        pf.Write(h, BinaryFile.CreationTime, BinaryFile.LastWriteTime);

        return pf;
    }
}

public class FilePairFileSystem : ComposeFileSystem
{
    public FilePairFileSystem(IFileSystem? fileSystem, bool owned = true) : base(fileSystem, owned)
    {
    }

    protected override IEnumerable<FileSystemItem> EnumerateItemsImpl(UPath path, SearchOption searchOption, SearchPredicate? searchPredicate)
    {
        throw new NotImplementedException();
    }
    
    protected override UPath ConvertPathToDelegate(UPath path) => path;
    protected override UPath ConvertPathFromDelegate(UPath path) => path;

    protected override IEnumerable<UPath> EnumeratePathsImpl(UPath path, string searchPattern, SearchOption searchOption, SearchTarget searchTarget)
    {
        // Iterate over all the files in the filesystem, and yield only the binaryfiles or binaryfile-equivalents

        if (searchPattern != "*")
            throw new NotSupportedException();
        if (searchOption != SearchOption.AllDirectories)
            throw new NotSupportedException();
        if (searchTarget != SearchTarget.File)
            throw new NotSupportedException();

        var fsi = FallbackSafe.GetFileSystemEntry(path);
        if (fsi is not DirectoryEntry d)
            throw new NotSupportedException();

        foreach (var fe in EnumerateFiles(d))
        {
            var p = fe.Path;
            if (p.IsPointerFilePath())
            {
                // this is a PointerFile
                var bfp = p.GetBinaryFilePath();
                if (FallbackSafe.FileExists(bfp))
                {
                    // 1. BinaryFile exists too - yield nothing here, the BinaryFile will be yielded
                    continue;
                }
                else
                {
                    // 2. BinaryFile does not exist
                    yield return bfp; // yield the path of the (nonexisting) binaryfile
                }
            }
            else
            {
                // this is a BinaryFile
                yield return p;
            }
        }
    }

    private static IEnumerable<FileEntry> EnumerateFiles(DirectoryEntry directory)
    {
        if (ShouldSkipDirectory(directory))
        {
            //logger.LogWarning("Skipping directory {directory} as it is hidden, system, or excluded", directory.FullName);
            yield break;
        }

        foreach (var fe in directory.EnumerateFiles())
        {
            if (ShouldSkipFile(fe))
            {
                //logger.LogWarning("Skipping file {file} as it is hidden, system, or excluded", fi.FullName);
                continue;
            }

            yield return fe;
        }

        foreach (var subDir in directory.EnumerateDirectories())
        {
            foreach (var file in EnumerateFiles(subDir))
            {
                yield return file;
            }
        }

        yield break;


        static bool ShouldSkipDirectory(DirectoryEntry dir) =>
            (dir.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0 ||
            ExcludedDirectories.Contains(dir.Name);

        static bool ShouldSkipFile(FileEntry file) =>
            (file.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0 ||
            ExcludedFiles.Contains(Path.GetFileName(file.FullName));
    }

    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase) { "@eaDir", "eaDir", "SynoResource" };
    private static readonly HashSet<string> ExcludedFiles = new(StringComparer.OrdinalIgnoreCase) { "autorun.ini", "thumbs.db", ".ds_store" };
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

    public PointerFile GetPointerFile()
    {
        var fe = new FileEntry(this.FileSystem, this.Path.GetPointerFilePath());
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
        var fe = new FileEntry(this.FileSystem, Path.GetBinaryFilePath());

        return BinaryFile.FromFileEntry(fe);
    }

    public Hash ReadHash()
    {
        var json = ReadAllBytes(); // throws a FileNotFoundException if not exists
        var pfc = JsonSerializer.Deserialize<PointerFileContents>(json);
        var h = new Hash(pfc!.BinaryHash);

        return h;
    }

    public void Write(Hash h, DateTime creationTime, DateTime lastWriteTime)
    {
        var pfc = new PointerFileContents(h.ToLongString());

        var json = JsonSerializer.SerializeToUtf8Bytes(pfc);
        WriteAllBytes(json);

        CreationTime = creationTime;
        LastWriteTime = lastWriteTime;
    }
    private record PointerFileContents(string BinaryHash);
}