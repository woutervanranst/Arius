using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Zio;
using SIO = System.IO;

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

public class StateRepository
{
    private readonly DbContextOptions<SqliteStateDatabaseContext> dbContextOptions;

    public StateRepository()
    {
        var stateDatabaseFile = new SIO.FileInfo("state.db");
        //stateDatabaseFile.Delete();

        var optionsBuilder = new DbContextOptionsBuilder<SqliteStateDatabaseContext>();
        dbContextOptions = optionsBuilder
            .UseSqlite($"Data Source={stateDatabaseFile.FullName}" /*+ ";Cache=Shared"*/, sqliteOptions => { sqliteOptions.CommandTimeout(60); })
            .Options;

        using var context = GetContext();
        //context.Database.Migrate();
        context.Database.EnsureCreated();
    }

    private SqliteStateDatabaseContext GetContext() => new(dbContextOptions, OnChanges);

    private void OnChanges(int changes) => hasChanges = hasChanges || changes > 0;
    private bool hasChanges;

    internal BinaryPropertiesDto? GetBinaryProperty(Hash h)
    {
        using var db = GetContext();

        return db.BinaryProperties.Find(h.Value);
    }

    internal void AddBinaryProperty(BinaryPropertiesDto bp)
    {
        using var db = GetContext();

        db.BinaryProperties.Add(bp);
        db.SaveChanges();
    }

    internal void UpsertPointerFileEntry(PointerFileEntryDto pfe)
    {
        using var db = GetContext();

        var existingPfe = db.PointerFileEntries.Find(pfe.Hash, pfe.RelativeName);

        if (existingPfe is null)
        {
            db.PointerFileEntries.Add(pfe);
        }
        else
        {
            existingPfe.CreationTimeUtc = pfe.CreationTimeUtc;
            existingPfe.LastWriteTimeUtc = pfe.LastWriteTimeUtc;
        }

        db.SaveChanges();
    }
}

public class ArchiveCommandHandler
{
    private readonly BlobContainerClient containerClient;
    private readonly string _passphrase;
    private readonly Dictionary<Hash, TaskCompletionSource> uploadingHashes = new();
    private readonly StateRepository stateRepo;
    private readonly SHA256Hasher hasher;

    public ArchiveCommandHandler(BlobContainerClient containerClient, string passphrase)
    {
        this.containerClient = containerClient;
        _passphrase = passphrase;

        stateRepo = new StateRepository();

        hasher = new SHA256Hasher("wouter");
    }

    

    public async Task UploadFileAsync(FilePair filePair)
    {
        //if (filePair.BinaryFile?.FileSystem is not PhysicalFileSystem)
        //    throw new ArgumentException("Source file must be in a PhysicalFileSystem", nameof(filePair));
        //if (filePair.PointerFile is not null && filePair.PointerFile.FileSystem is not PhysicalFileSystem)
        //    throw new ArgumentException("Source file must be in a PhysicalFileSystem", nameof(filePair));

        // 1. Hash the file
        var h = await hasher.GetHashAsync(filePair);

        // 2. Check if the Binary is already present. If the binary is not present, check if the Binary is already being uploaded
        var (needsToBeUploaded, uploadTask) = GetUploadStatus(h);
        
        
        // 3. Upload the Binary, if needed
        if (needsToBeUploaded)
        {
            var bbc = containerClient.GetBlockBlobClient($"chunks/{h.ToLongString()}");

            var ss = filePair.BinaryFile.OpenRead();
            var ts = await OpenWriteAsync(bbc, throwOnExists: false);

            await ss.CopyToCompressedEncryptedAsync(ts, _passphrase);

            await bbc.SetAccessTierAsync(AccessTier.Cool);

            // Add to db
            stateRepo.AddBinaryProperty(new BinaryPropertiesDto { Hash = h.Value, StorageTier = StorageTier.Cool });

            // remove from temp
            MarkAsUploaded(h);
        }
        else
        {
            await uploadTask;
        }

        // Write the PointerFileEntry
        stateRepo.UpsertPointerFileEntry(new PointerFileEntryDto { Hash = h.Value, RelativeName = filePair.BinaryFile.FullName });

        // Write the Pointer
        CreatePointerFile(filePair.BinaryFile, h);
    }

    private (bool needsToBeUploaded, Task uploadTask) GetUploadStatus(Hash h)
    {
        var bp = stateRepo.GetBinaryProperty(h);

        lock (uploadingHashes)
        {
            if (bp is null)
            {
                if (uploadingHashes.TryGetValue(h, out var tcs))
                {
                    // Already uploading
                    return (false, tcs.Task);
                }
                else
                {
                    // To be uploaded
                    tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    uploadingHashes.Add(h, tcs);

                    return (true, tcs.Task);
                }
            }
            else
            {
                // Already uploaded
                return (false, Task.CompletedTask);
            }
        }
    }

    private void MarkAsUploaded(Hash h)
    {
        lock (uploadingHashes)
        {
            uploadingHashes.Remove(h, out var tcs);
            tcs.SetResult();
        }
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

    private void CreatePointerFile(BinaryFile bf, Hash h)
    {
        var pf = bf.GetPointerFile();

        var pfc = new PointerFileContents(h.ToLongString());

        var json = JsonSerializer.SerializeToUtf8Bytes(pfc);
        pf.WriteAllBytes(json);

        pf.CreationTime = bf.CreationTime;
        pf.LastWriteTime = bf.LastWriteTime;

        //var xx = ReadPointerFile(bf.FileSystem, pfPath);
        //if (bf.FileSystem.FileExists(pfPath))
        //{
        //}
        //else
        //{
        //}

        //var pfPath = bf.Path.ChangeExtension($"{bf.ExtensionWithDot}{PointerFile.Extension}");
        //var pfc = new PointerFileContents(h.ToLongString());

        //var json = JsonSerializer.SerializeToUtf8Bytes(pfc);
        //bf.FileSystem.WriteAllBytes(pfPath, json);

        //bf.FileSystem.SetCreationTime(pfPath, bf.CreationTime);
        //bf.FileSystem.SetLastWriteTime(pfPath, bf.LastWriteTime);
    }

    //private (PointerFile pf, Hash h) ReadPointerFile(IFileSystem fs, UPath pfPath)
    //{
    //    var pf = new PointerFile(fs, pfPath);
    //    var json = pf.ReadAllBytes(); // throws a FileNotFoundException if not exists
    //    var pfc = JsonSerializer.Deserialize<PointerFileContents>(json);
    //    var h = new Hash(pfc.BinaryHash);

    //    return (pf, h);
    //}

    private record PointerFileContents(string BinaryHash);
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