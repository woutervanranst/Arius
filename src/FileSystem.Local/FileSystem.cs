using System;

namespace FileSystem.Local;

public class FileSystem
{
    private static TIO.IFileSystem Instance { get; } = new TIO.FileSystem();

    public Root CreateRoot(RootPathSegment root) => new(Instance, root);
}

public class Root
{
    private readonly TIO.IFileSystem _fileSystem;
    private readonly RootPathSegment _root;

    internal Root(TIO.IFileSystem fileSystem, RootPathSegment root)
    {
        _fileSystem = fileSystem;
        _root = root;
    }

    public File CreateFile(RelativePathSegment relativeName) => new(_fileSystem, _root, relativeName);
}

public class File : IFile
{
    private readonly TIO.IFileSystem _fileSystem;
    private readonly FullNamePathSegment _fullNamePath;

    internal File(TIO.IFileSystem fileSystem, FullNamePathSegment fullNamePath)
    {
        _fileSystem = fileSystem;
        _fullNamePath = fullNamePath;
    }

    internal File(TIO.IFileSystem fileSystem, RootPathSegment root, RelativePathSegment relativeName) : this(fileSystem, root + relativeName)
    {
    }

    public FullNamePathSegment FullNamePath => _fullNamePath;
    public string? Path => _fileSystem.Path.GetDirectoryName(FullNamePath);
    public string Name => _fileSystem.Path.GetFileName(FullNamePath);
    public bool Exists => _fileSystem.File.Exists(FullNamePath);
    public DateTime? CreationTimeUtc { get; set; }
    public DateTime? LastWriteTimeUtc { get; set; }
    public long Length => _fileSystem.FileInfo.New(FullNamePath).Length;


    private static readonly SIO.FileStreamOptions smallFileStreamReadOptions = new() { Mode = SIO.FileMode.Open, Access = SIO.FileAccess.Read, Share = SIO.FileShare.Read, BufferSize = 1024 };
    private static readonly SIO.FileStreamOptions largeFileStreamReadOptions = new() { Mode = SIO.FileMode.Open, Access = SIO.FileAccess.Read, Share = SIO.FileShare.Read, BufferSize = 32768, Options = SIO.FileOptions.Asynchronous };
    public SIO.Stream OpenRead() => _fileSystem.File.Open(_fullNamePath, Length <= 1024 ? smallFileStreamReadOptions : largeFileStreamReadOptions);

    private static readonly SIO.FileStreamOptions smallFileStreamWriteOptions = new() { Mode = SIO.FileMode.OpenOrCreate, Access = SIO.FileAccess.Write, Share = SIO.FileShare.None, BufferSize = 1024 };
    private static readonly SIO.FileStreamOptions largeFileStreamWriteOptions = new() { Mode = SIO.FileMode.OpenOrCreate, Access = SIO.FileAccess.Write, Share = SIO.FileShare.None, BufferSize = 32768, Options = SIO.FileOptions.Asynchronous };
    public SIO.Stream OpenWrite() => _fileSystem.File.Open(_fullNamePath, Length <= 1024 ? smallFileStreamWriteOptions : largeFileStreamWriteOptions);

    public IFile CopyTo(NamePathSegment destinationName)
    {
        var newFullName = _fileSystem.Path.Combine(Path!, destinationName);
        _fileSystem.File.Copy(FullNamePath, newFullName);
        return new File(_fileSystem, newFullName);
    }

    public IFile CopyTo(IFile destination)
    {
        _fileSystem.File.Copy(FullNamePath, destination.FullNamePath);
        return destination;
    }

    public void Delete() => _fileSystem.File.Delete(FullNamePath);
}