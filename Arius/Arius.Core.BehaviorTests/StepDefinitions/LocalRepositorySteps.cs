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
        }

        record RelatedFiles(FileInfo Source, FileInfo Archive, FileInfo Restore);


        //[Given(@"a local folder with only file {word}")]
        //public void GivenLocalFolderWithOnlyFile(string fileId)
        //{
        //    ClearDirectories();
        //    GivenLocalFolderWithFile(fileId);
        //}

        [Given(@"a local folder with BinaryFile {word}")]
        public void GivenLocalFolderWithFile(string fileId)
        {
            var f0 = GetOrCreateSourceFile(fileId);

            var f1 = f0.CopyTo(directories.SourceDirectory, directories.ArchiveTestDirectory);

            scenarioContext[fileId] = new RelatedFiles(f0, f1, null);
            scenarioContext.AddLocalRepoStats();
        }

        [Given("a local folder with BinaryFile {word} of size ARCHIVE_TIER_LIMIT")]
        public void GivenALocalFolderWithFileFileOfSizeARCHIVE_TIER_LIMIT(string fileId)
        {
            throw new PendingStepException();
        }


        ////[BeforeScenario]
        //public void ClearDirectories()
        //{
        //    directories.ArchiveTestDirectory.Clear();
        //    directories.RestoreTestDirectory.Clear();
        //}


        [Given(@"a local folder with BinaryFile {word} duplicate of BinaryFile {word}")]
        public void GivenALocalFolderWithFileDuplicateOf(string newFileId, string originalFileId)
        {
            var f0 = GetOrCreateSourceFile(originalFileId);
            var f1 = f0.CopyTo(directories.SourceDirectory, $"Duplicate of {f0.Name}");
            f1.CreationTimeUtc += TimeSpan.FromSeconds(-10); //Put it in the past for Linux
            f1.LastWriteTimeUtc += TimeSpan.FromSeconds(-10); //Put it in the past for Linux

            var f2 = f1.CopyTo(directories.SourceDirectory, directories.ArchiveTestDirectory);

            scenarioContext[newFileId] = new RelatedFiles(f1, f2, null);
        }

        [Given("a duplicate Pointer {word} of file {word}")]
        public void WhenADuplicatePointerOfFileFile(string pointerId, string fileId)
        {
            var bfi0 = ((RelatedFiles)scenarioContext[fileId]).Archive;

            var (pf0, _) = GetPointerInfo(bfi0);
            var pfi1 = new FileInfo(Path.Combine(directories.ArchiveTestDirectory.FullName, $"Duplicate of {pf0.Name}"));
            File.Copy(pf0.FullName, pfi1.FullName);

            pfi1.CreationTimeUtc += TimeSpan.FromSeconds(-10); //Put it in the past for Linux
            pfi1.LastWriteTimeUtc += TimeSpan.FromSeconds(-10); //Put it in the past for Linux

            scenarioContext[pointerId] = pfi1;
        }



        //[When(@"the local folder is cleared")]
        //public void WhenTheLocalFolderIsCleared()
        //{
        //    ClearDirectories();
        //}

        [When(@"BinaryFile {word} and its PointerFile are deleted")]
        public void FileIsDeleted(string fileId)
        {
            var fi = ((RelatedFiles)scenarioContext[fileId]).Archive;
            fi.Delete();

            var (pfi, _) = GetPointerInfo(fi);
            pfi.Delete();
        }






        [Then(@"BinaryFile {word} does not have a PointerFile and the PointerFileEntry is marked as deleted")]
        public void ThenFileDoesNotHaveAPointerFile(string binaryFileId)
        {
            var fi = ((RelatedFiles)scenarioContext[binaryFileId]).Archive;

            var (pf, pfe) = GetPointerInfo(fi);

            pf.Should().BeNull();
            pfe.IsDeleted.Should().BeTrue();
        }

        [Then("BinaryFile {word} has a PointerFile and the PointerFileEntry is marked as exists")]
        public void ThenBinaryFileHasAPointerFileAndThePointerFileEntryIsMarkedAsExists(string binaryFileId)
        {
            var fi = ((RelatedFiles)scenarioContext[binaryFileId]).Archive;

            var (pf, pfe) = GetPointerInfo(fi);

            pf.Should().NotBeNull();
            pfe.IsDeleted.Should().BeFalse();
        }

        //[Then(@"the PointerFileEntry for {word} is marked as deleted")]
        //public void ThenThePointerFileEntryForFileIsMarkedAsDeleted(string fileId)
        //{
        //    var fi = ((RelatedFiles)scenarioContext[fileId]).Archive;

        //    var (pf, pfe) = GetPointerInfo(fi);

        //    pfe.IsDeleted.Should().BeTrue();
        //}




        //[Then("{int} PointerFile(s) exist")]
        //public void ThenPointerFilesExist(int p0)
        //{
        //    directories.ArchiveTestDirectory.GetPointerFileInfos().Count().Should().Be(p0);
        //}

        [Then("{int} additional PointerFile(s) exist")]
        public void ThenPointerFilesExist(int x)
        {
            var x0 = scenarioContext.GetLocalRepoStats().SkipLast(1).Last().PointerFileInfos.Length;
            var x1 = scenarioContext.GetLocalRepoStats().Last().PointerFileInfos.Length;

            Assert.AreEqual(x0 + x, x1);
        }



        //[Then(@"all local files have PointerFiles and PointerFileEntries")]
        //public void ThenAllLocalFilesHavePointerFilesAndPointerFileEntries()
        //{
        //    foreach (var bfi in directories.ArchiveTestDirectory.GetBinaryFileInfos())
        //        IsValidPointerFile(bfi);
        //}

        //[Then(@"all local PointerFiles have PointerFileEntries")]
        //public void ThenAllLocalPointerFilesHavePointerFileEntries()
        //{
        //    foreach (var pfi in directories.ArchiveTestDirectory.GetPointerFileInfos())
        //        IsValidPointerFile(pfi);
        //}

        /// <summary>
        /// if a (Binary)FileInfo -> checks whether it has a valid PointerFile
        /// if a PointerFile -> chekcs whether is it valid
        /// </summary>
        /// <param name="fi"></param>
        private void IsValidPointerFile(FileInfo fi)
        {
            var (pf, pfe) = GetPointerInfo(fi);

            pf.Should().NotBeNull();

            pfe.Should().NotBeNull();
            pfe.IsDeleted.Should().BeFalse();

            fi.CreationTimeUtc.Should().Be(pfe.CreationTimeUtc);
            fi.LastWriteTimeUtc.Should().Be(pfe.LastWriteTimeUtc);
        }

        //[Then("the PointerFile for file {word} exists")]
        //public void ThenThePointerFileForFileFileExists(string fileId)
        //{
        //    var bfi = ((RelatedFiles)scenarioContext[fileId]).Archive;
        //    IsValidPointerFile(bfi);
        //}

        [Then("the PointerFile {word} exists")]
        public void ThenThePointerFilePointerExists(string pointerId)
        {
            var pfi = (FileInfo)scenarioContext[pointerId];
            IsValidPointerFile(pfi);
        }









        private FileInfo GetOrCreateSourceFile(string fileId, string? name = default, int? sizeInBytes = default)
        {
            if (!scenarioContext.ContainsKey(fileId))
            {
                if (name == default)
                    name = Path.GetRandomFileName();

                if (sizeInBytes == default)
                    sizeInBytes = 512000 + 1;

                var f = CreateRandomFile(Path.Combine(directories.SourceDirectory.FullName, "dir 1", name), sizeInBytes!.Value);

                scenarioContext[fileId] = new RelatedFiles(f, null, null);
            }

            return ((RelatedFiles)scenarioContext[fileId]).Source;
        }

        private static FileInfo CreateRandomFile(string fileFullName, int sizeInBytes)
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
