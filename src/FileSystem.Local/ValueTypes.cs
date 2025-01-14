using System;
using TIO = System.IO.Abstractions;
using SIO = System.IO;

namespace FileSystem.Local;

public record PathSegment
{
    public PathSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Path segment cannot be null or empty.", nameof(value));

        Value = value;
    }

    internal string Value { get; }

    public static implicit operator PathSegment(string path)
    {
        return new PathSegment(path);
    }

    public static implicit operator string(PathSegment segment)
    {
        return segment.Value;
    }

    public static PathSegment operator +(PathSegment left, PathSegment right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        return new PathSegment(SIO.Path.Combine(left.Value, right.Value));
    }

    public override string ToString()
    {
        return Value;
    }
}

public record RootPathSegment : PathSegment
{
    public RootPathSegment(string value) : base(value)
    {
        if (!SIO.Path.IsPathRooted(value))
            throw new ArgumentException("Root must be a rooted path.", nameof(value));
    }

    public static FullNamePathSegment operator +(RootPathSegment left, RelativePathSegment right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        return new FullNamePathSegment(left, right);
    }
}

public record RelativePathSegment : PathSegment
{
    public RelativePathSegment(string value) : base(value)
    {
        if (SIO.Path.IsPathRooted(value))
            throw new ArgumentException("Relative name cannot be a rooted value.", nameof(value));
    }
}

public record FullNamePathSegment : PathSegment
{
    public FullNamePathSegment(string fullName) : base(fullName)
    {
    }

    public FullNamePathSegment(RootPathSegment root, RelativePathSegment relativeName) : base((string)(root + relativeName))
    {
    }

    public static implicit operator FullNamePathSegment(string path)
    {
        return new FullNamePathSegment(path);
    }

    public static implicit operator string(FullNamePathSegment segment)
    {
        return segment.Value;
    }
}

public record NamePathSegment : PathSegment
{
    public NamePathSegment(string name) : base(name)
    {
        if (name.Contains(SIO.Path.PathSeparator))
            throw new ArgumentException("Name cannot contain path separators.", nameof(name));
    }
}

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