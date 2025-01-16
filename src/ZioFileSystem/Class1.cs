using Zio;
using Zio.FileSystems;

namespace ZioFileSystem;

public static class Class1
{
    public static void ha()
    {
        var x = new UPath("C:\\temp\\bla.txt");

        var fs = new PhysicalFileSystem();
        //var xx = new FileEntry(fs, x);

        var zz = fs.EnumeratePaths("/mnt/c");

        //xx.)

        var afs = new AggregateFileSystem();

        //afs.OpenFile()
    }

}

public record PathSegment
{
    public static readonly UPath Empty = new(string.Empty);

    private readonly UPath _value;

    private PathSegment(string value)
    {
        var pp = new PhysicalFileSystem().ConvertPathFromInternal(value);
        var p = System.IO.Path.GetPathRoot(value);
        _value = new UPath(value);
    }
    private PathSegment(UPath value)
    {
        _value = value;
    }

    public static implicit operator PathSegment(string path) => new(path);

    public static PathSegment operator +(PathSegment left, PathSegment right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        return new PathSegment(left._value / right._value);
    }

    public PathSegment GetPath() => new(_value.GetDirectory());

    public PathSegment GetRelativePath(PathSegment relativeTo) => Path.GetRelativePath(relativeTo._value.FullName, _value.FullName);

    public bool IsRooted => _value.IsAbsolute;

    public override string ToString() => _value.FullName;
}

public interface IFile
{
    PathSegment FullNamePath { get; }
    PathSegment? Path { get; }
    string Name { get; }
    bool Exists { get; }

    /// <summary>
    /// Get the CreationTime of the file in UTC. Null if the file does not exist
    /// </summary>
    DateTime? CreationTimeUtc { get; set; }

    /// <summary>
    /// Get the LastWriteTime of the file in UTC. Null if the file does not exist
    /// </summary>
    DateTime? LastWriteTimeUtc { get; set; }

    //bool IsPointerFile { get; }
    //bool IsBinaryFile { get; }

    //string BinaryFileFullName { get; }
    //string PointerFileFullName { get; }

    long Length { get; }

    //string       GetRelativeName(string relativeTo);
    //string       GetRelativeNamePlatformNeutral(string relativeTo);
    //string       GetRelativeName(DirectoryInfo relativeTo);
    //string       GetRelativeNamePlatformNeutral(DirectoryInfo relativeTo);
    //IPointerFile GetPointerFile(DirectoryInfo root);
    //IBinaryFile GetBinaryFile(DirectoryInfo root);

    Stream OpenRead();
    Stream OpenWrite();

    IFile CopyTo(NamePathSegment destinationName);
    IFile CopyTo(IFile destination);
    void Delete();
}