using Arius.Core.Extensions;
using Arius.Core.Models;
using Arius.Core.Services;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.Extensions.DependencyInjection;
using TechTalk.SpecFlow;

namespace Arius.Core.BehaviorTests2
{
    [Binding]
    static class FileSystem
    {
        [BeforeTestRun(Order = 2)] //run after the RemoteRepository is initialized, and the BlobContainerClient is available for DI
        private static void ClassInit()
        {
            root = new DirectoryInfo(Path.Combine(Path.GetTempPath(), "arius"));
            runRoot = root.CreateSubdirectory(Arius.ContainerName);
            ArchiveDirectory = runRoot.CreateSubdirectory("archive");
            RestoreDirectory = runRoot.CreateSubdirectory("restore");
            TempDirectory = runRoot.CreateSubdirectory("temp");
        }

        private static DirectoryInfo root;
        private static DirectoryInfo runRoot;
        public static DirectoryInfo ArchiveDirectory { get; private set; }
        public static DirectoryInfo RestoreDirectory { get; private set; }
        public static DirectoryInfo TempDirectory { get; private set; }

        [AfterTestRun]
        private static void ClassCleanup()
        {
            // Delete local temp
            foreach (var d in root.GetDirectories())
                d.Delete(true);
        }


        private static string GetFileName(DirectoryInfo root, string relativeName) => Path.Combine(root.FullName, relativeName);
        public static FileInfo GetFileInfo(DirectoryInfo root, string relativeName) => new FileInfo(GetFileName(root, relativeName));
        public static PointerFile GetPointerFile(DirectoryInfo root, string relativeName) => Arius.PointerService.Value.GetPointerFile(root, GetFileInfo(root, relativeName));
        public static bool Exists(DirectoryInfo root, string relativeName) => File.Exists(GetFileName(root, relativeName));
        public static long Length(DirectoryInfo root, string relativeName) => new FileInfo(GetFileName(root, relativeName)).Length;
        public static void CreateFile(string relativeName, int sizeInBytes)
        {
            var fileName = GetFileName(ArchiveDirectory, relativeName);

            // https://stackoverflow.com/q/4432178/1582323
            var f = new FileInfo(fileName);
            if (!f.Directory.Exists)
                f.Directory.Create();

            byte[] data = new byte[sizeInBytes];
            var rng = new Random();
            rng.NextBytes(data);
            File.WriteAllBytes(fileName, data);
        }





        public static void CopyArchiveBinaryFileToRestoreDirectory(string relativeBinaryFile)
        {
            var apf = GetPointerFile(ArchiveDirectory, relativeBinaryFile);
            var apfi = new FileInfo(apf.FullName);

            apfi.CopyTo(ArchiveDirectory, RestoreDirectory);
        }




        

        public static void RestoreDirectoryEqualToArchiveDirectory(bool compareBinaryFile, bool comparePointerFile)
        {
            IEnumerable<FileInfo> archiveFiles, restoredFiles;

            if (compareBinaryFile && comparePointerFile)
            {
                archiveFiles = ArchiveDirectory.GetAllFileInfos();
                restoredFiles = RestoreDirectory.GetAllFileInfos();
            }
            else if (compareBinaryFile)
            {
                archiveFiles = ArchiveDirectory.GetBinaryFileInfos();
                restoredFiles = RestoreDirectory.GetBinaryFileInfos();
            }
            else if (comparePointerFile)
            {
                archiveFiles = ArchiveDirectory.GetPointerFileInfos();
                restoredFiles = RestoreDirectory.GetPointerFileInfos();
            }
            else
                throw new ArgumentException();

            archiveFiles.SequenceEqual(restoredFiles, new FileComparer()).Should().BeTrue();
        }

        public static void RestoreBinaryFileEqualToArchiveBinaryFile(string relativeBinaryFile)
        {
            var afi = GetFileInfo(ArchiveDirectory, relativeBinaryFile);
            var rfi = GetFileInfo(RestoreDirectory, relativeBinaryFile);

            new FileComparer().Equals(afi, rfi);

        }








        private class FileComparer : IEqualityComparer<FileInfo>
        {
            public FileComparer() { }

            public bool Equals(FileInfo x, FileInfo y)
            {
                return x.Name == y.Name &&
                       x.Length == y.Length &&
                       x.CreationTimeUtc == y.CreationTimeUtc &&
                       x.LastWriteTimeUtc == y.LastWriteTimeUtc &&
                       SHA256Hasher.GetHashValue(x.FullName, "").Equals(SHA256Hasher.GetHashValue(y.FullName, ""));
            }

            public int GetHashCode(FileInfo obj)
            {
                return HashCode.Combine(obj.Name, obj.Length, obj.LastWriteTimeUtc, SHA256Hasher.GetHashValue(obj.FullName, ""));
            }
        }
    }
}