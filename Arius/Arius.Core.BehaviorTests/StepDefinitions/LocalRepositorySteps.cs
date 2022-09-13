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
    public record Directories(DirectoryInfo Root, DirectoryInfo RunRoot, DirectoryInfo SourceDirectory, DirectoryInfo ArchiveTestDirectory, DirectoryInfo RestoreTestDirectory);

    [Binding]
    class LocalRepositorySteps : LocalTestBase
    {
        [BeforeTestRun(Order = 2)] //run after the RemoteRepository is initialized, and the BlobContainerClient is available for DI
        public static void InitializeLocalRepository(IObjectContainer oc, BlobContainerClient bcc)
        {
            var root = new DirectoryInfo(Path.Combine(Path.GetTempPath(), "arius"));
            var runRoot = root.CreateSubdirectory(bcc.Name);
            var sourceDirectory = runRoot.CreateSubdirectory("source");
            var archiveTestDirectory = runRoot.CreateSubdirectory("archive");
            var restoreTestDirectory = runRoot.CreateSubdirectory("restore");

            oc.RegisterInstanceAs(new Directories(root, runRoot, sourceDirectory, archiveTestDirectory, restoreTestDirectory));
        }

        public LocalRepositorySteps(ScenarioContext sc, Directories directories) : base(sc, directories)
        {
            file1 = new(() => CreateRandomFile(Path.Combine(directories.SourceDirectory.FullName, "dir 1", "file 1.txt"), 512000 + 1)); //make it an odd size to test buffer edge cases
        }

        [BeforeScenario]
        public void ClearDirectories()
        {
            directories.ArchiveTestDirectory.Clear();
            directories.RestoreTestDirectory.Clear();
        }

        private Lazy<FileInfo> file1;

        [Given(@"a local archive with 1 file (.*)")]
        public void GivenOneLocalFile(string name)
        {
            var f = file1.Value.CopyTo(directories.SourceDirectory, directories.ArchiveTestDirectory);

            scenarioContext[name] = f;
        }

        //[Then(@"the file has a PointerFile")]
        //public void TheFileHasAPointerFile()
        //{
        //    var fi = (FileInfo)scenarioContext[ScenarioContextIds.FILE1.ToString()];

        //    var (pf, pfe) = GetPointerInfo(fi);

        //    Assert.IsNotNull(pfe);
        //}




        [Then(@"all local files have PointerFiles and PointerFileEntries")]
        public void ThenAllLocalFilesHavePointerFiles()
        {
            foreach (var bfi in directories.ArchiveTestDirectory.GetAllFileInfos())
            {
                if (bfi.IsPointerFile())
                    continue;

                var (pf, pfe) = GetPointerInfo(bfi);

                pf.Should().NotBeNull();
                
                pfe.Should().NotBeNull();
                pfe.IsDeleted.Should().BeFalse();

                bfi.CreationTimeUtc.Should().Be(pfe.CreationTimeUtc);
                bfi.LastWriteTimeUtc.Should().Be(pfe.LastWriteTimeUtc);
            }
        }

        [When(@"the local archive is cleared")]
        public void WhenTheLocalArchiveIsCleared()
        {
            directories.ArchiveTestDirectory.Clear();
        }

        [Then(@"(.*) does not have a PointerFile")]
        public void ThenFileDoesNotHaveAPointerFile(string name)
        {
            var fi = (FileInfo)scenarioContext[name];

            var (pf, pfe) = GetPointerInfo(fi);

            pf.Should().BeNull();
        }

        [Then(@"the PointerFileEntry for (.*) is marked as deleted")]
        public void ThenThePointerFileEntryForFileIsMarkedAsDeleted(string name)
        {
            var fi = (FileInfo)scenarioContext[name];

            var (pf, pfe) = GetPointerInfo(fi);

            pfe.IsDeleted.Should().BeTrue();
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
        public static void OneTimeTearDown(Directories directories)
        {
            // Delete local temp
            foreach (var d in directories.Root.GetDirectories())
                d.Delete(true);
        }
    }
}
