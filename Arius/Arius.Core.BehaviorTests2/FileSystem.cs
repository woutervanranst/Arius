﻿using Arius.Core.Models;
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
        }

        private static DirectoryInfo root;
        private static DirectoryInfo runRoot;
        public static DirectoryInfo ArchiveDirectory { get; private set; }
        public static DirectoryInfo RestoreDirectory { get; private set; }

        [AfterTestRun]
        private static void ClassCleanup()
        {
            // Delete local temp
            foreach (var d in root.GetDirectories())
                d.Delete(true);
        }

        private static string GetFileName(DirectoryInfo root, string relativeName) => Path.Combine(root.FullName, relativeName);
        public static FileInfo GetFileInfo(DirectoryInfo root, string relativeName) => new FileInfo(GetFileName(root, relativeName));

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


        public static PointerFile GetPointerFile(DirectoryInfo root, string relativeName)
        {
            return Arius.PointerService.Value.GetPointerFile(root, GetFileInfo(root, relativeName));
        }


    }
}