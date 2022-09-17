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
            runRoot = root.CreateSubdirectory(AriusRepository.ContainerName);
            TestDirectory = runRoot.CreateSubdirectory("test");
        }

        private static DirectoryInfo root;
        private static DirectoryInfo runRoot;
        public static DirectoryInfo TestDirectory { get; private set; }

        [AfterTestRun]
        private static void ClassCleanup()
        {
            // Delete local temp
            foreach (var d in root.GetDirectories())
                d.Delete(true);
        }

        private static string GetFileName(string relativeName) => Path.Combine(TestDirectory.FullName, relativeName);
        private static FileInfo GetFileInfo(string relativeName) => new FileInfo(GetFileName(relativeName));

        public static bool Exists(string relativeName) => File.Exists(GetFileName(relativeName));
        public static long Length(string relativeName) => new FileInfo(GetFileName(relativeName)).Length;
        public static void CreateFile(string relativeName, int sizeInBytes)
        {
            var fileName = GetFileName(relativeName);

            // https://stackoverflow.com/q/4432178/1582323
            var f = new FileInfo(fileName);
            if (!f.Directory.Exists)
                f.Directory.Create();

            byte[] data = new byte[sizeInBytes];
            var rng = new Random();
            rng.NextBytes(data);
            File.WriteAllBytes(fileName, data);
        }


        public static PointerFile GetPointerFile(string relativeName)
        {
            return AriusRepository.PointerService.Value.GetPointerFile(TestDirectory, GetFileInfo(relativeName));
        }


    }
}