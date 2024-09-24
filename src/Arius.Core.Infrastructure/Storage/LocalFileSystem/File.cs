using Arius.Core.Domain;
using Arius.Core.Domain.Extensions;
using Arius.Core.Domain.Storage;
using Arius.Core.Domain.Storage.FileSystem;
using System.Text.Json;

namespace Arius.Core.Infrastructure.Storage.LocalFileSystem;

public class File : IFile // TODO make internal
{
    private readonly string fullName;

    protected File(string fullName)
    {
        fullName = fullName.ToPlatformSpecificPath();

        if (!System.IO.Path.IsPathFullyQualified(fullName))
            throw new ArgumentException($"'{fullName}' is not a fully qualified path");

        this.fullName = fullName;
    }

    public static IFile FromFullName(string fullName)                             => new File(fullName);
    public static IFile FromRelativeName(DirectoryInfo root, string relativeName) => new File(System.IO.Path.Combine(root.FullName, relativeName));

    public string  FullName            => fullName;
    public string? Path                => System.IO.Path.GetDirectoryName(fullName);
    //public string? PathPlatformNeutral => Path?.ToPlatformNeutralPath();
    public string  Name                => System.IO.Path.GetFileName(fullName);

    public bool Exists => System.IO.File.Exists(FullName);

    public DateTime? CreationTimeUtc
    {
        get => Exists ? System.IO.File.GetCreationTimeUtc(FullName) : null;
        set
        {
            if (Exists)
                if (value is not null)
                    System.IO.File.SetCreationTimeUtc(FullName, value.Value); //FileInfo does not work on Linux according to https://stackoverflow.com/a/17126045/1582323
                else
                {
                    throw new ArgumentException($"CreationTimeUtc is null");
                }
            else
                throw new InvalidOperationException("The file does not exist");
        }
    }

    public DateTime? LastWriteTimeUtc
    {
        get => Exists ? System.IO.File.GetLastWriteTimeUtc(FullName) : null;
        set
        {
            if (Exists)
                System.IO.File.SetLastWriteTimeUtc(FullName, value.Value); //FileInfo does not work on Linux according to https://stackoverflow.com/a/17126045/1582323
            else
                throw new InvalidOperationException("The file does not exist");
        }
    }

    public long Length => new FileInfo(FullName).Length;

    public bool IsPointerFile => fullName.EndsWith(IPointerFile.Extension, StringComparison.OrdinalIgnoreCase);
    public bool IsBinaryFile  => !IsPointerFile;

    public string BinaryFileFullName => fullName.RemoveSuffix(IPointerFile.Extension, StringComparison.OrdinalIgnoreCase);
    //public string BinaryFileFullNamePlatformNeutral => BinaryFileFullName.ToPlatformNeutralPath();

    public string PointerFileFullName => IsPointerFile ? FullName : FullName + IPointerFile.Extension;
    //public string PointerFileFullNamePlatformNeutral => PointerFileFullName.ToPlatformNeutralPath();

    public IPointerFile GetPointerFile(DirectoryInfo root) => PointerFile.FromFullName(root, PointerFileFullName);
    public IBinaryFile  GetBinaryFile(DirectoryInfo root)  => BinaryFile.FromFullName(root, BinaryFileFullName);

    public Stream OpenRead() => Length <= 1024 ?
        new FileStream(fullName, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1024 /* benchmarked -- do not add extra options */) :
        new FileStream(fullName, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 32768, useAsync: true); // Enable async I/O for better performance with large files

    public Stream OpenWrite() => Length <= 1024 ?
        new FileStream(fullName, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None, bufferSize: 1024,  useAsync: false) :
        new FileStream(fullName, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None, bufferSize: 32768, useAsync: true); // Enable async I/O for better performance with large files

    /// <summary>
    /// Copy this File to the same directory to the destinationName
    /// </summary>
    /// <param name="destinationName"></param>
    public IFile CopyTo(string destinationName)
    {
        var newFullName = System.IO.Path.Combine(Path!, destinationName);
        System.IO.File.Copy(FullName, newFullName);
        return FromFullName(newFullName);
    }

    public void Delete() => System.IO.File.Delete(fullName);

    //public virtual bool Equals(IFile? other)
    //{
    //    return other is not null &&
    //           string.Equals(this.FullName, other.FullName, StringComparison.OrdinalIgnoreCase);
    //}

    //public override int GetHashCode() => FullName.GetHashCode();

    public override string ToString() => FullName;
}

public class StateDatabaseFile : File, IStateDatabaseFile
{
    private StateDatabaseFile(string fullName, RepositoryVersion version) : base(fullName)
    {
        if (!fullName.EndsWith(IStateDatabaseFile.Extension, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"'{fullName}' is not a valid StateDatabaseFile");

        this.Version = version;
    }

    public static IStateDatabaseFile FromRepositoryVersion(AriusConfiguration config, RemoteRepositoryOptions options, RepositoryVersion version)
    {
        var stateDbFolder = config.GetLocalStateDatabaseFolderForRepositoryOptions(options);
        return new StateDatabaseFile(System.IO.Path.Combine(stateDbFolder.FullName, GetFileSystemName(version)), version);
    }

    public static IStateDatabaseFile FromFullName(DirectoryInfo stateDbFolder, string fullName)
    {
        var version = GetVersion(fullName);
        return new StateDatabaseFile(fullName, version);
    }

    public RepositoryVersion Version { get; }

    private static string GetFileSystemName(RepositoryVersion version)
    {
        return $"{version.Name.Replace(":", "-")}{IStateDatabaseFile.Extension}";
    }

    private static RepositoryVersion GetVersion(string name)
    {
        var n = System.IO.Path.GetFileName(name).RemoveSuffix(IStateDatabaseFile.Extension);
        if (DateTime.TryParseExact(n, "yyyy-MM-ddTHH-mm-ss", null, System.Globalization.DateTimeStyles.AssumeUniversal, out var parsedDateTime))
        {
            return parsedDateTime;
        }
        else
        {
            return new RepositoryVersion { Name = n };
        }

    }
}

public abstract class RelativeFile : File, IRelativeFile
{
    protected RelativeFile(DirectoryInfo root, string fullName) : base(fullName)
    {
        Root = root;
    }

    public DirectoryInfo Root                        { get; }
    public string        RelativeName                => System.IO.Path.GetRelativePath(Root.FullName, FullName);
    public string        RelativeNamePlatformNeutral => RelativeName.ToPlatformNeutralPath();
    
    public IPointerFile  GetPointerFile()            => base.GetPointerFile(Root);
    public IBinaryFile   GetBinaryFile()             => base.GetBinaryFile(Root);

    //public string GetRelativeNamePlatformNeutral(string relativeTo)        => GetRelativeName(relativeTo).ToPlatformNeutralPath();
    //public string GetRelativeName(DirectoryInfo relativeTo)                => GetRelativeName(relativeTo.FullName);
    //public string GetRelativeNamePlatformNeutral(DirectoryInfo relativeTo) => GetRelativeNamePlatformNeutral(relativeTo.FullName);

    //public virtual bool Equals(RelativeFile? other)
    //{
    //    return other is not null
    //           && base.Equals((File)other)
    //           && string.Equals(this.Root?.FullName, other.Root?.FullName, StringComparison.OrdinalIgnoreCase);
    //}

    //public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), Root?.FullName);



    public override string ToString() => RelativeName;
}

public class PointerFile : RelativeFile, IPointerFile
{
    protected PointerFile(DirectoryInfo root, string fullName) : base(root, fullName)
    {
        if (!fullName.EndsWith(IPointerFile.Extension, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"'{fullName}' is not a valid PointerFile");
    }

    //public static PointerFile FromFileInfo(DirectoryInfo root, FileInfo fi)             => new(root, fi);
    public static     IPointerFile FromFullName(DirectoryInfo root, string fullName)         => new PointerFile(root, fullName);
    public new static IPointerFile FromRelativeName(DirectoryInfo root, string relativeName) => new PointerFile(root, System.IO.Path.Combine(root.FullName, relativeName));

    public string BinaryFileRelativeName                => RelativeName.RemoveSuffix(IPointerFile.Extension, StringComparison.OrdinalIgnoreCase);
    public string BinaryFileRelativeNamePlatformNeutral => BinaryFileRelativeName.ToPlatformNeutralPath();

    public override string ToString() => RelativeName;
}

public class PointerFileWithHash : PointerFile, IPointerFileWithHash
{
    protected PointerFileWithHash(DirectoryInfo root, string fullName, Hash hash) : base(root, fullName)
    {
        Hash = hash;
    }

    public static IPointerFileWithHash FromFullName(DirectoryInfo root, string fullName, Hash h)         => new PointerFileWithHash(root, fullName,                                            h);
    public static IPointerFileWithHash FromRelativeName(DirectoryInfo root, string relativeName, Hash h) => new PointerFileWithHash(root, System.IO.Path.Combine(root.FullName, relativeName), h);
    public static IPointerFileWithHash FromBinaryFileWithHash(IBinaryFileWithHash bfwh)                  => new PointerFileWithHash(bfwh.Root, bfwh.FullName, bfwh.Hash);

    /// <summary>
    /// Get a PointerFile with Hash by reading the value in the PointerFile
    /// </summary>
    /// <returns></returns>
    public static IPointerFileWithHash FromExistingPointerFile(IPointerFile pf)
    {
        if (!System.IO.File.Exists(pf.FullName))
            throw new ArgumentException($"'{pf.FullName}' does not exist");

        try
        {
            var json = System.IO.File.ReadAllBytes(pf.FullName);
            var pfc = JsonSerializer.Deserialize<PointerFileContents>(json);
            var h = new Hash(pfc.BinaryHash);

            return PointerFileWithHash.FromFullName(pf.Root, pf.FullName, h);
        }
        catch (JsonException e)
        {
            throw new ArgumentException($"'{pf.FullName}' is not a valid PointerFile", e);
        }
    }
    /// <summary>
    /// Write the PointerFile to disk
    /// </summary>
    public static IPointerFileWithHash Create(IBinaryFileWithHash bfwh) => Create(bfwh.Root, bfwh.RelativeName + IPointerFile.Extension, bfwh.Hash, bfwh.CreationTimeUtc.Value, bfwh.LastWriteTimeUtc.Value);
    public static IPointerFileWithHash Create(DirectoryInfo root, PointerFileEntry pfe) => Create(root, pfe.RelativeName + IPointerFile.Extension, pfe.Hash, pfe.CreationTimeUtc, pfe.LastWriteTimeUtc);
    public static IPointerFileWithHash Create(DirectoryInfo root, string relativeName, Hash hash, DateTime creationTimeUtc, DateTime lastWriteTimeUtc)
    {
        var pfwh = FromRelativeName(root, relativeName, hash);

        Directory.CreateDirectory(pfwh.Path);

        var pfc = new PointerFileContents(hash.Value.BytesToHexString());
        var json = JsonSerializer.SerializeToUtf8Bytes(pfc); //ToUtf8 is faster https://docs.microsoft.com/en-us/dotnet/standard/serialization/system-text-json-how-to?pivots=dotnet-6-0#serialize-to-utf-8
        System.IO.File.WriteAllBytes(pfwh.FullName, json);

        pfwh.CreationTimeUtc  = creationTimeUtc;
        pfwh.LastWriteTimeUtc = lastWriteTimeUtc;

        return pfwh;
    }
    private record PointerFileContents(string BinaryHash);


    public Hash Hash { get; }

    public new IPointerFileWithHash GetPointerFileWithHash() => PointerFileWithHash.FromFullName(Root, PointerFileFullName, Hash);
    public new IBinaryFileWithHash  GetBinaryFileWithHash()  => BinaryFileWithHash.FromFullName(Root, BinaryFileFullName, Hash);

    public override string ToString() => RelativeName;
}

public class BinaryFile : RelativeFile, IBinaryFile
{
    protected BinaryFile(DirectoryInfo root, string fullName) : base(root, fullName)
    {
        if (fullName.EndsWith(IPointerFile.Extension, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"'{fullName}' is a PointerFile not a BinaryFile");
    }

    public static     IBinaryFile FromFullName(DirectoryInfo root, string fullName)         => new BinaryFile(root, fullName);
    public new static IBinaryFile FromRelativeName(DirectoryInfo root, string relativeName) => new BinaryFile(root, System.IO.Path.Combine(root.FullName, relativeName));

    public string PointerFileRelativeName                => RelativeName + IPointerFile.Extension;
    public string PointerFileRelativeNamePlatformNeutral => PointerFileRelativeName.ToPlatformNeutralPath();

    public override string ToString() => RelativeName;
}

public class BinaryFileWithHash : BinaryFile, IBinaryFileWithHash // to private
{
    protected BinaryFileWithHash(DirectoryInfo root, string fullName, Hash hash) : base(root, fullName)
    {
        Hash = hash;
    }

    public static IBinaryFileWithHash FromFullName(DirectoryInfo root, string fullName, Hash h)         => new BinaryFileWithHash(root,    fullName,                                            h);
    public static IBinaryFileWithHash FromRelativeName(DirectoryInfo root, string relativeName, Hash h) => new BinaryFileWithHash(root,    System.IO.Path.Combine(root.FullName, relativeName), h);
    public static IBinaryFileWithHash FromBinaryFile(IBinaryFile bf, Hash h)                            => new BinaryFileWithHash(bf.Root, bf.FullName,                                         h);

    public Hash Hash { get; }

    //public PointerFileWithHash GetPointerFileWithHash() => PointerFileWithHash.FromFullName(root, FullName + PointerFile.Extension, Hash);
    public PointerFileEntry GetPointerFileEntry() => PointerFileEntry.FromBinaryFileWithHash(this);

    public new IPointerFileWithHash GetPointerFileWithHash() => PointerFileWithHash.FromFullName(Root, PointerFileFullName, Hash);
    public new IBinaryFileWithHash  GetBinaryFileWithHash()  => BinaryFileWithHash.FromFullName(Root, BinaryFileFullName, Hash);

    public override string ToString() => RelativeName;
}

public class FilePair : IFilePair
{
    protected FilePair(IPointerFile pointerFile, IBinaryFile binaryFile)
    {
        PointerFile = pointerFile;
        BinaryFile  = binaryFile;
    }

    public static FilePair FromBinaryFile(IBinaryFile binaryFile)
    {
        ArgumentNullException.ThrowIfNull(binaryFile);
        if (binaryFile.Exists is false)
            throw new ArgumentException($"'{binaryFile.FullName}' does not exist");

        var pointerFile = binaryFile.GetPointerFile();
        return new FilePair(pointerFile, binaryFile);
    }

    public static FilePair FromPointerFile(IPointerFile pointerFile)
    {
        ArgumentNullException.ThrowIfNull(pointerFile);
        if (pointerFile.Exists is false)
            throw new ArgumentException($"'{pointerFile.FullName}' does not exist");

        var binaryFile = pointerFile.GetBinaryFile();
        return new FilePair(pointerFile, binaryFile);
    }

    public static FilePair FromFilePair(IPointerFile pointerFile, IBinaryFile binaryFile)
    {
        ArgumentNullException.ThrowIfNull(pointerFile);
        ArgumentNullException.ThrowIfNull(binaryFile);

        return new FilePair(pointerFile, binaryFile);
    }

    public IPointerFile PointerFile { get; }
    public IBinaryFile BinaryFile { get; }

    public DirectoryInfo Root => BinaryFile?.Root ?? PointerFile!.Root;
    public string RelativeName => BinaryFile?.RelativeName ?? PointerFile!.RelativeName; // TODO dit verschilt afh of er een BF of een PF inzit?
    public string RelativeNamePlatformNeutral => BinaryFile?.RelativeNamePlatformNeutral ?? PointerFile!.RelativeNamePlatformNeutral; // TODO dit verschilt afh of er een BF of een PF inzit?

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

    public bool IsBinaryFileWithPointerFile => PointerFile.Exists && BinaryFile.Exists;
    public bool IsPointerFileOnly => PointerFile.Exists && !BinaryFile.Exists;
    public bool IsBinaryFileOnly => !PointerFile.Exists && BinaryFile.Exists;

    public bool HasExistingPointerFile => PointerFile.Exists;
    public bool HasExistingBinaryFile => BinaryFile.Exists;

    public override string ToString()
    {
        if (PointerFile.Exists && BinaryFile.Exists)
            return $"FilePair PF+BF '{RelativeName}'";
        else if (!PointerFile.Exists && BinaryFile.Exists)
            return $"FilePair BF '{RelativeName}'";
        else if (PointerFile.Exists && !BinaryFile.Exists)
            return $"FilePair PF '{RelativeName}'";
        else
            throw new InvalidOperationException("PointerFile and BinaryFile are both null");
    }
}

public class FilePairWithHash : FilePair, IFilePairWithHash
{
    protected FilePairWithHash(IPointerFileWithHash pointerFile, IBinaryFileWithHash binaryFile) : base(pointerFile, binaryFile)
    {
        this.PointerFile = pointerFile;
        this.BinaryFile  = binaryFile;
    }

    public static FilePairWithHash FromBinaryFile(IBinaryFileWithHash binaryFile)
    {
        ArgumentNullException.ThrowIfNull(binaryFile);
        if (binaryFile.Exists is false)
            throw new ArgumentException($"'{binaryFile.FullName}' does not exist");

        var pointerFile = binaryFile.GetPointerFileWithHash();
        return new FilePairWithHash(pointerFile, binaryFile);
    }

    public static FilePairWithHash FromPointerFile(IPointerFileWithHash pointerFile)
    {
        ArgumentNullException.ThrowIfNull(pointerFile);
        if (pointerFile.Exists is false)
            throw new ArgumentException($"'{pointerFile.FullName}' does not exist");

        var binaryFile = pointerFile.GetBinaryFileWithHash();
        return new FilePairWithHash(pointerFile, binaryFile);
    }

    public static FilePairWithHash FromFilePair(IPointerFileWithHash pointerFile, IBinaryFileWithHash binaryFile)
    {
        ArgumentNullException.ThrowIfNull(pointerFile);
        ArgumentNullException.ThrowIfNull(binaryFile);

        return new FilePairWithHash(pointerFile, binaryFile);
    }

    public new IPointerFileWithHash PointerFile { get; }
    public new IBinaryFileWithHash  BinaryFile  { get; }

    public Hash Hash => BinaryFile?.Hash ?? PointerFile!.Hash;

    public override string ToString()
    {
        if (PointerFile.Exists && BinaryFile.Exists)
            return $"FilePairWithHash PF+BF '{RelativeName}' ({PointerFile.Hash.ToShortString()})";
        else if (!PointerFile.Exists && BinaryFile.Exists)
            return $"FilePairWithHash BF '{RelativeName}' ({BinaryFile.Hash.ToShortString()})";
        else if (PointerFile.Exists && !BinaryFile.Exists)
            return $"FilePairWithHash PF '{RelativeName}' ({PointerFile.Hash.ToShortString()})";
        else
            throw new InvalidOperationException("PointerFile and BinaryFile are both null");
    }
}