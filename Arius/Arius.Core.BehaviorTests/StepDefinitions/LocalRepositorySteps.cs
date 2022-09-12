using Azure.Storage.Blobs;
using BoDi;
using NUnit.Framework;
using System;
using System.Diagnostics;
using TechTalk.SpecFlow;
using Arius.Core.Extensions;
using Arius.Core.BehaviorTests.Extensions;
using Arius.Core.Repositories;
using static Arius.Core.BehaviorTests.StepDefinitions.ScenarioContextExtensions;

namespace Arius.Core.BehaviorTests.StepDefinitions
{
    public record Directories(DirectoryInfo root, DirectoryInfo SourceFolder, DirectoryInfo ArchiveTestDirectory, DirectoryInfo RestoreTestDirectory);

    [Binding]
    public class LocalRepositorySteps
    {
        [BeforeTestRun(Order = 2)]
        public static void InitializeLocalRepository(IObjectContainer oc, BlobContainerClient bcc)
        {
            var unitTestRoot = new DirectoryInfo(Path.Combine(Path.GetTempPath(), "arius", bcc.Name));
            var sourceFolder = unitTestRoot.CreateSubdirectory("source");
            var archiveTestDirectory = unitTestRoot.CreateSubdirectory("archive");
            var restoreTestDirectory = unitTestRoot.CreateSubdirectory("restore");

            oc.RegisterInstanceAs(new Directories(unitTestRoot, sourceFolder, archiveTestDirectory, restoreTestDirectory));
        }

        public LocalRepositorySteps(ScenarioContext sc, Directories directories)
        {
            this.sc = sc;
            this.directories = directories;

            file1 = new(() => CreateRandomFile(Path.Combine(directories.SourceFolder.FullName, "dir 1", "file 1.txt"), 512000 + 1)); //make it an odd size to test buffer edge cases
        }

        [BeforeScenario]
        public void ClearDirectories()
        {
            directories.ArchiveTestDirectory.Clear();
            directories.RestoreTestDirectory.Clear();
        }

        private readonly ScenarioContext sc;
        private readonly Directories directories;
        private Lazy<FileInfo> file1;

        [Given(@"a local archive with 1 file")]
        public void GivenOneLocalFile()
        {
            var f = file1.Value.CopyTo(directories.SourceFolder, directories.ArchiveTestDirectory);

            sc[ScenarioContextIds.FILE1.ToString()] = f;
        }

        [Then(@"the file has a PointerFile")]
        public void TheFileHasAPointerFile()
        {
            var repo = sc.ScenarioContainer.Resolve<Repository>();
            var fi = (FileInfo)sc[ScenarioContextIds.FILE1.ToString()];

        }









        private static FileInfo CreateRandomFile(string fileFullName, int sizeInBytes) // TODO make private
        {
            // https://stackoverflow.com/q/4432178/1582323

            var f = new FileInfo(fileFullName);
            if (!f.Directory.Exists)
                f.Directory.Create();

            byte[] data = new byte[sizeInBytes];
            var rng = new Random();
            rng.NextBytes(data);
            File.WriteAllBytes(fileFullName, data);

            return f;
        }

        [AfterTestRun]
        public static async Task OneTimeTearDown(Directories directories)
        {
            // Delete local temp
            foreach (var d in directories.root.GetDirectories())
                d.Delete(true);
        }
    }
}
