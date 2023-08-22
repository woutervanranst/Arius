using System.Diagnostics;
using Arius.Core.Models;
using Arius.Core.Services;
using System.Text.RegularExpressions;
using Arius.Core.Services.Chunkers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Arius.Core.BehaviorTests;

[Binding]
static class FileSystem
{
    [BeforeTestRun(Order = 2)] //run after the RemoteRepository is initialized, and the BlobContainerClient is available for DI
    private static void ClassInit()
    {
        root             = new DirectoryInfo(Path.Combine(Path.GetTempPath(), "arius"));
        runRoot          = root.CreateSubdirectory(TestSetup.Repository.Options.ContainerName);
        ArchiveDirectory = runRoot.CreateSubdirectory("archive");
        RestoreDirectory = runRoot.CreateSubdirectory("restore");
        TempDirectory    = runRoot.CreateSubdirectory("temp");
    }

    private static DirectoryInfo root;
    private static DirectoryInfo runRoot;
    public static  DirectoryInfo ArchiveDirectory { get; private set; }
    public static  DirectoryInfo RestoreDirectory { get; private set; }
    public static  DirectoryInfo TempDirectory    { get; private set; }

    [AfterTestRun]
    private static void ClassCleanup()
    {
        // Delete local temp
        foreach (var d in root.GetDirectories())
            d.Delete(true);
    }


    private static string GetFileName(DirectoryInfo root, string relativeName) => Path.Combine(root.FullName, relativeName);
    public static FileInfo GetFileInfo(DirectoryInfo root, string relativeName) => new (GetFileName(root, relativeName));
    public static PointerFile GetPointerFile(DirectoryInfo root, string relativeName) => TestSetup.FileService.GetExistingPointerFile(root, FileSystemService.GetPointerFileInfo(Path.Combine(root.FullName, relativeName)));
    public static async Task<BinaryFile> GetBinaryFileAsync(DirectoryInfo root, string relativeName) => await TestSetup.FileService.GetExistingBinaryFileAsync(GetPointerFile(root, relativeName), true);
    public static bool Exists(DirectoryInfo root, string relativeName) => File.Exists(GetFileName(root, relativeName));
    public static long Length(DirectoryInfo root, string relativeName) => new FileInfo(GetFileName(root, relativeName)).Length;

    public static void CreateBinaryFileIfNotExists(string relativeName, string size)
    {
        var sizeInBytes = SizeInBytes(size);

        if (Exists(ArchiveDirectory, relativeName))
        {
            if (Length(ArchiveDirectory, relativeName) != sizeInBytes)
                throw new ArgumentException("File already exists and is of different size");

            // Reuse the file that already exists
            return;
        }
        else
        {
            var fileName = GetFileName(ArchiveDirectory, relativeName);
                
            // https://stackoverflow.com/q/4432178/1582323
            var f = new FileInfo(fileName);
            if (!f.Directory.Exists)
                f.Directory.Create();

            var data = new byte[sizeInBytes];
            var rng = new Random();
            rng.NextBytes(data);
            File.WriteAllBytes(fileName, data);
        }
    }

    public static int SizeInBytes(string size) =>
        size switch
        {
            "BELOW_ARCHIVE_TIER_LIMIT" => 12 * 1024 + 1, // 12 KB
            "ABOVE_ARCHIVE_TIER_LIMIT" => 1024 * 1024 + 1, // Note: the file needs to be big enough (> 1 MB) to put into Archive storage (see ChunkBlobBase.SetAccessTierPerPolicyAsync)

            "BELOW_CHUNKSIZE_LIMIT" => ByteBoundaryChunker.DEFAULT_MIN_CHUNK_SIZE / 2,
            "APPROX_TEN_CHUNKS" => ByteBoundaryChunker.DEFAULT_MIN_CHUNK_SIZE * 100,

            _ when
                // see https://stackoverflow.com/a/3513858
                // see https://codereview.stackexchange.com/a/67506
                int.TryParse(Regex.Match(size, @"(?<size>\d*) KB").Groups["size"].Value, out var size0)
                => size0 * 1024,
            _ =>
                throw new ArgumentOutOfRangeException()
        };

    public static void DuplicateBinaryFile(string relativeBinaryName, string sourceRelativeBinaryName)
    {
        if (GetFileInfo(ArchiveDirectory, relativeBinaryName).Exists)
            return;

        var bfi = GetFileInfo(ArchiveDirectory, sourceRelativeBinaryName);
        bfi.CopyTo(Path.Combine(ArchiveDirectory.FullName, relativeBinaryName));
    }

    public static void DuplicatePointerFile(string relativeBinaryName, string sourceRelativeBinaryName)
    {
        var pf0 = GetPointerFile(ArchiveDirectory, sourceRelativeBinaryName);
        var pfi0 = new FileInfo(pf0.FullName);

        var pfn1 = Path.Combine(ArchiveDirectory.FullName, BinaryFileInfo.GetPointerFileName(relativeBinaryName));
        pfi0.CopyTo(pfn1);
    }

    public static void Move(string sourceRelativeBinaryName, string targetRelativeBinaryName, bool moveBinary, bool movePointer)
    {
        var bfi0 = GetFileInfo(ArchiveDirectory, sourceRelativeBinaryName);
        var bfi1 = GetFileInfo(ArchiveDirectory, targetRelativeBinaryName);

        bfi1.Directory.Create();
            
        if (moveBinary)
            bfi0.MoveTo(bfi1.FullName);

        if (movePointer)
        {
            var pfi0 = new FileInfo(GetPointerFile(ArchiveDirectory, sourceRelativeBinaryName).FullName);
            var pfi1 = BinaryFileInfo.GetPointerFileName(bfi1.FullName); // construct the new name based on the path of the binary
                
            pfi0.MoveTo(pfi1);
        }
    }



    public static void CopyArchiveBinaryFileToRestoreDirectory(string relativeBinaryFile)
    {
        var apf = GetPointerFile(ArchiveDirectory, relativeBinaryFile);
        var apfi = new FileInfo(apf.FullName);

        apfi.CopyTo(ArchiveDirectory, RestoreDirectory);
    }



    public static void RestoreDirectoryEqualToArchiveDirectory(bool compareBinaryFile, bool comparePointerFile)
    {
        IEnumerable<FileInfoBase> archiveFiles, restoredFiles;
        //var fsf = new FileSystemService(NullLogger<FileSystemService>.Instance);

        if (compareBinaryFile && comparePointerFile)
        {
            archiveFiles  = fileSystemService.GetAllFileInfos(ArchiveDirectory);
            restoredFiles = fileSystemService.GetAllFileInfos(RestoreDirectory);
        }
        else if (compareBinaryFile)
        {
            archiveFiles  = fileSystemService.GetBinaryFileInfos(ArchiveDirectory);
            restoredFiles = fileSystemService.GetBinaryFileInfos(RestoreDirectory);
        }
        else if (comparePointerFile)
        {
            archiveFiles  = fileSystemService.GetPointerFileInfos(ArchiveDirectory);
            restoredFiles = fileSystemService.GetPointerFileInfos(RestoreDirectory);
        }
        else
            throw new ArgumentException();

        archiveFiles.SequenceEqual(restoredFiles, new FileInfoBaseComparer()).Should().BeTrue();
    }

    public static void RestoreBinaryFileEqualToArchiveBinaryFile(string relativeBinaryFile)
    {
        var afi = FileSystemService.GetBinaryFileInfo(ArchiveDirectory, relativeBinaryFile);
        var rfi = FileSystemService.GetBinaryFileInfo(RestoreDirectory, relativeBinaryFile);

        new FileInfoBaseComparer().Equals(afi, rfi);
    }



    private static readonly FileSystemService            fileSystemService = new FileSystemService(new NullLogger<FileSystemService>());
    public static           IEnumerable<BinaryFileInfo>  GetBinaryFileInfos(this DirectoryInfo di)  => fileSystemService.GetBinaryFileInfos(di);
    public static           IEnumerable<PointerFileInfo> GetPointerFileInfos(this DirectoryInfo di) => fileSystemService.GetPointerFileInfos(di);


    //private class FileInfoComparer : IEqualityComparer<FileInfo>
    //{
    //    private readonly SHA256Hasher hasher = new("somesalt");

    //    public bool Equals(FileInfo x, FileInfo y)
    //    {
    //        return x.Name == y.Name &&
    //               x.Length == y.Length &&
    //               x.CreationTimeUtc == y.CreationTimeUtc &&
    //               x.LastWriteTimeUtc == y.LastWriteTimeUtc &&
    //               hasher.GetBinaryHash(x.FullName).Equals(hasher.GetBinaryHash(y.FullName));
    //    }

    //    public int GetHashCode(FileInfo obj)
    //    {
    //        return HashCode.Combine(obj.Name, obj.Length, obj.LastWriteTimeUtc, hasher.GetBinaryHash(obj.FullName));
    //    }
    //}
    private class FileInfoBaseComparer : IEqualityComparer<FileInfoBase>
    {
        private readonly SHA256Hasher hasher = new("somesalt");

        public bool Equals(FileInfoBase x, FileInfoBase y)
        {
            return x.Name == y.Name &&
                   x.Length == y.Length &&
                   x.CreationTimeUtc == y.CreationTimeUtc &&
                   x.LastWriteTimeUtc == y.LastWriteTimeUtc &&
                   hasher.GetBinaryHash(x.FullName).Equals(hasher.GetBinaryHash(y.FullName));
        }

        public int GetHashCode(FileInfoBase obj)
        {
            return HashCode.Combine(obj.Name, obj.Length, obj.LastWriteTimeUtc, hasher.GetBinaryHash(obj.FullName));
        }
    }
}