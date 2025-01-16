using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Zio;
using SIO = System.IO;

namespace ZioFileSystem.AzureBlobStorage;

public record FilePair(BinaryFile? BinaryFile, PointerFile? PointerFile);

public class BinaryFile : FileEntry
{
    public BinaryFile(IFileSystem fileSystem, UPath path) : base(fileSystem, path)
    {
    }

    private static readonly SIO.FileStreamOptions smallFileStreamReadOptions = new() { Mode = SIO.FileMode.Open, Access = SIO.FileAccess.Read, Share = SIO.FileShare.Read, BufferSize = 1024 };
    private static readonly SIO.FileStreamOptions largeFileStreamReadOptions = new() { Mode = SIO.FileMode.Open, Access = SIO.FileAccess.Read, Share = SIO.FileShare.Read, BufferSize = 32768, Options = SIO.FileOptions.Asynchronous };
    public SIO.Stream OpenRead() => SIO.File.Open(this.ConvertPathToInternal(), Length <= 1024 ? smallFileStreamReadOptions : largeFileStreamReadOptions);

    //    private static readonly SIO.FileStreamOptions smallFileStreamWriteOptions = new() { Mode = SIO.FileMode.OpenOrCreate, Access = SIO.FileAccess.Write, Share = SIO.FileShare.None, BufferSize = 1024 };
    //    private static readonly SIO.FileStreamOptions largeFileStreamWriteOptions = new() { Mode = SIO.FileMode.OpenOrCreate, Access = SIO.FileAccess.Write, Share = SIO.FileShare.None, BufferSize = 32768, Options = SIO.FileOptions.Asynchronous };
    //    public SIO.Stream OpenWrite() => _fileSystem.File.Open(_fullNamePath, Length <= 1024 ? smallFileStreamWriteOptions : largeFileStreamWriteOptions);
}

public class PointerFile : FileEntry
{
    public PointerFile(IFileSystem fileSystem, UPath path) : base(fileSystem, path)
    {
    }
}

public class ArchiveCommandHandler
{
    private readonly BlobContainerClient containerClient;
    private readonly string _passphrase;
    private readonly DbContextOptions<SqliteStateDatabaseContext> dbContextOptions;
    private readonly SHA256Hasher hasher;
    private readonly ConcurrentDictionary<Hash, bool> uploadingHashes = new();

    public ArchiveCommandHandler(BlobContainerClient containerClient, string passphrase)
    {
        this.containerClient = containerClient;
        _passphrase = passphrase;

        var stateDatabaseFile = new SIO.FileInfo("state.db");
        //stateDatabaseFile.Delete();
        
        var optionsBuilder = new DbContextOptionsBuilder<SqliteStateDatabaseContext>();
        dbContextOptions = optionsBuilder
            .UseSqlite($"Data Source={stateDatabaseFile.FullName}" /*+ ";Cache=Shared"*/, sqliteOptions => { sqliteOptions.CommandTimeout(60); })
            .Options;

        using var context = GetContext();
        //context.Database.Migrate();
        context.Database.EnsureCreated();

        hasher = new SHA256Hasher("wouter");
    }

    private SqliteStateDatabaseContext GetContext() => new(dbContextOptions, OnChanges);

    private void OnChanges(int changes) => hasChanges = hasChanges || changes > 0;
    private bool hasChanges;

    public async Task UploadFileAsync(FilePair filePair)
    {
        //if (filePair.BinaryFile?.FileSystem is not PhysicalFileSystem)
        //    throw new ArgumentException("Source file must be in a PhysicalFileSystem", nameof(filePair));
        //if (filePair.PointerFile is not null && filePair.PointerFile.FileSystem is not PhysicalFileSystem)
        //    throw new ArgumentException("Source file must be in a PhysicalFileSystem", nameof(filePair));

        await using var c = GetContext();

        // 1. Hash the file
        var h = await hasher.GetHashAsync(filePair);

        // 2. Check if the Binary is already present. If the binary is not present, check if the Binary is already being uploaded
        var bp = c.BinaryProperties.Find(h.Value);
        bool needsToBeUploaded = bp is null && uploadingHashes.TryAdd(h, true);
        
        // 3. Upload the Binary, if needed
        if (needsToBeUploaded)
        {
            var bbc = containerClient.GetBlockBlobClient($"chunks/{h.ToLongString()}");

            var ss = filePair.BinaryFile.OpenRead();
            var ts = await OpenWriteAsync(bbc, throwOnExists: false);

            await ss.CopyToCompressedEncryptedAsync(ts, _passphrase);

            await bbc.SetAccessTierAsync(AccessTier.Cool);

            // Add to db
            c.BinaryProperties.Add(new BinaryPropertiesDto { Hash = h.Value, StorageTier = StorageTier.Cool });
            c.SaveChanges();

            // remove from temp
            uploadingHashes.Remove(h, out var _);
        }

        // Write the PointerFileEntry
        c.PointerFileEntries.Add(new PointerFileEntryDto { Hash = h.Value, RelativeName = filePair.BinaryFile.FullName });
        c.SaveChanges();

        // Write the Pointer

    }

    private static async Task<Stream> OpenWriteAsync(BlockBlobClient bbc, /*string contentType = ICryptoService.ContentType, */IDictionary<string, string>? metadata = default, bool throwOnExists = true, CancellationToken cancellationToken = default)
    {
        var bbowo = new BlockBlobOpenWriteOptions();

        //NOTE the SDK only supports OpenWriteAsync with overwrite: true, therefore the ThrowOnExistOptions workaround
        if (throwOnExists)
            bbowo.OpenConditions = new BlobRequestConditions { IfNoneMatch = new ETag("*") }; // as per https://github.com/Azure/azure-sdk-for-net/issues/24831#issue-1031369473
        if (metadata is not null)
            bbowo.Metadata = metadata;
        //bbowo.HttpHeaders = new BlobHttpHeaders { ContentType = contentType };

        return await bbc.OpenWriteAsync(overwrite: true, options: bbowo, cancellationToken: cancellationToken);
    }
}


public class  DeduplicatedEncryptedBlobStorageFileSystem : IFileSystem
{
   


    public void Dispose()
    {
        throw new NotImplementedException();
    }

    public void CreateDirectory(UPath path)
    {
        throw new NotImplementedException();
    }

    public bool DirectoryExists(UPath path)
    {
        throw new NotImplementedException();
    }

    public void MoveDirectory(UPath srcPath, UPath destPath)
    {
        throw new NotImplementedException();
    }

    public void DeleteDirectory(UPath path, bool isRecursive)
    {
        throw new NotImplementedException();
    }

    public void CopyFile(UPath srcPath, UPath destPath, bool overwrite)
    {
        throw new NotImplementedException();
    }

    public void ReplaceFile(UPath srcPath, UPath destPath, UPath destBackupPath, bool ignoreMetadataErrors)
    {
        throw new NotImplementedException();
    }

    public long GetFileLength(UPath path)
    {
        throw new NotImplementedException();
    }

    public bool FileExists(UPath path)
    {
        throw new NotImplementedException();
    }

    public void MoveFile(UPath srcPath, UPath destPath)
    {
        throw new NotImplementedException();
    }

    public void DeleteFile(UPath path)
    {
        throw new NotImplementedException();
    }

    public SIO.Stream OpenFile(UPath path, SIO.FileMode mode, SIO.FileAccess access, SIO.FileShare share = SIO.FileShare.None)
    {
        throw new NotImplementedException();
    }

    public SIO.FileAttributes GetAttributes(UPath path)
    {
        throw new NotImplementedException();
    }

    public void SetAttributes(UPath path, SIO.FileAttributes attributes)
    {
        throw new NotImplementedException();
    }

    public DateTime GetCreationTime(UPath path)
    {
        throw new NotImplementedException();
    }

    public void SetCreationTime(UPath path, DateTime time)
    {
        throw new NotImplementedException();
    }

    public DateTime GetLastAccessTime(UPath path)
    {
        throw new NotImplementedException();
    }

    public void SetLastAccessTime(UPath path, DateTime time)
    {
        throw new NotImplementedException();
    }

    public DateTime GetLastWriteTime(UPath path)
    {
        throw new NotImplementedException();
    }

    public void SetLastWriteTime(UPath path, DateTime time)
    {
        throw new NotImplementedException();
    }

    public void CreateSymbolicLink(UPath path, UPath pathToTarget)
    {
        throw new NotImplementedException();
    }

    public bool TryResolveLinkTarget(UPath linkPath, out UPath resolvedPath)
    {
        throw new NotImplementedException();
    }

    public IEnumerable<UPath> EnumeratePaths(UPath path, string searchPattern, SIO.SearchOption searchOption, SearchTarget searchTarget)
    {
        throw new NotImplementedException();
    }

    public IEnumerable<FileSystemItem> EnumerateItems(UPath path, SIO.SearchOption searchOption, SearchPredicate? searchPredicate = null)
    {
        throw new NotImplementedException();
    }

    public bool CanWatch(UPath path)
    {
        throw new NotImplementedException();
    }

    public IFileSystemWatcher Watch(UPath path)
    {
        throw new NotImplementedException();
    }

    public string ConvertPathToInternal(UPath path)
    {
        throw new NotImplementedException();
    }

    public UPath ConvertPathFromInternal(string systemPath)
    {
        throw new NotImplementedException();
    }

    public (IFileSystem FileSystem, UPath Path) ResolvePath(UPath path)
    {
        throw new NotImplementedException();
    }
}

//public class Blob : FileSystemItem
//{
//    public Blob(UPath path, long length, DateTime creationTime, DateTime lastAccessTime, DateTime lastWriteTime)
//        : base(path, creationTime, lastAccessTime, lastWriteTime)
//    {
//        Length = length;
//    }

//    public long Length { get; }
//}