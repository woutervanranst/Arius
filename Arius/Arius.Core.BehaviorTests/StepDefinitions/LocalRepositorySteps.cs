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
using Arius.Core.Models;

namespace Arius.Core.BehaviorTests.StepDefinitions
{
    record Directories(DirectoryInfo Root, DirectoryInfo RunRoot, DirectoryInfo SourceDirectory, DirectoryInfo ArchiveTestDirectory, DirectoryInfo RestoreTestDirectory);

    record RelatedFiles(FileInfo Source, FileInfo Archive, FileInfo Restore);

    [Binding]
    class LocalRepositorySteps : TestBase
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

        public LocalRepositorySteps(ScenarioContext sc) : base(sc)
        {
        }



        //[Given(@"a local folder with only file {word}")]
        //public void GivenLocalFolderWithOnlyFile(string fileId)
        //{
        //    ClearDirectories();
        //    GivenLocalFolderWithFile(fileId);
        //}

        [Given(@"a local folder with BinaryFile {word}")]
        public void GivenLocalFolderWithFile(string binaryFileId)
        {
            NewMethod(binaryFileId);
        }
        [Given("a local folder with BinaryFile {word} of size {word}")]
        public void GivenALocalFolderWithFileFileOfSizeARCHIVE_TIER_LIMIT(string binaryFileId, string size)
        {
            int? sizeInBytes = size switch
            {
                "BELOW_ARCHIVE_TIER_LIMIT" => null,
                "ABOVE_ARCHIVE_TIER_LIMIT" => 1024 * 1024 + 1,  // Note: the file needs to be big enough (> 1 MB) to put into Archive storage (see ChunkBlobBase.SetAccessTierPerPolicyAsync) 
                _ => throw new NotImplementedException()
            };

            NewMethod(binaryFileId, sizeInBytes);
        }

        private void NewMethod(string binaryFileId, int? sizeInBytes = null)
        {
            var f0 = GetOrCreateSourceFile(binaryFileId, sizeInBytes: sizeInBytes);

            var f1 = f0.CopyTo(directories.SourceDirectory, directories.ArchiveTestDirectory);

            scenarioContext[binaryFileId] = new RelatedFiles(f0, f1, null);
            scenarioContext.AddLocalRepoStats();
        }


        ////[BeforeScenario]
        //public void ClearDirectories()
        //{
        //    directories.ArchiveTestDirectory.Clear();
        //    directories.RestoreTestDirectory.Clear();
        //}


        [Given(@"a local folder with BinaryFile {word} duplicate of BinaryFile {word}")]
        public void GivenALocalFolderWithFileDuplicateOf(string newBinaryFileId, string originalBinaryFileId)
        {
            var f0 = GetOrCreateSourceFile(originalBinaryFileId);
            var f1 = f0.CopyTo(directories.SourceDirectory, $"Duplicate of {f0.Name}");
            f1.CreationTimeUtc += TimeSpan.FromSeconds(-10); //Put it in the past for Linux
            f1.LastWriteTimeUtc += TimeSpan.FromSeconds(-10); //Put it in the past for Linux

            var f2 = f1.CopyTo(directories.SourceDirectory, directories.ArchiveTestDirectory);

            scenarioContext[newBinaryFileId] = new RelatedFiles(f1, f2, null);
        }

        [Given("a duplicate PointerFile {word} of the Pointer of BinaryFile {word}")]
        public async Task WhenADuplicatePointerOfFileFile(string pointerFileId, string binaryFileId)
        {
            var bfi0 = ((RelatedFiles)scenarioContext[binaryFileId]).Archive;

            var (pf0, _) = await GetPointerInfoAsync(bfi0);
            var pfi1 = new FileInfo(Path.Combine(directories.ArchiveTestDirectory.FullName, $"Duplicate of {pf0.Name}"));
            File.Copy(pf0.FullName, pfi1.FullName);

            pfi1.CreationTimeUtc += TimeSpan.FromSeconds(-10); //Put it in the past for Linux
            pfi1.LastWriteTimeUtc += TimeSpan.FromSeconds(-10); //Put it in the past for Linux

            scenarioContext[pointerFileId] = pfi1;
        }



        //[When(@"the local folder is cleared")]
        //public void WhenTheLocalFolderIsCleared()
        //{
        //    ClearDirectories();
        //}

        [When(@"BinaryFile {word} and its PointerFile are deleted")]
        public async Task FileIsDeleted(string binaryFileId)
        {
            var bfi = ((RelatedFiles)scenarioContext[binaryFileId]).Archive;
            bfi.Delete();

            var (pfi, _) = await GetPointerInfoAsync(bfi);
            pfi.Delete();
        }






        [Then(@"BinaryFile {word} does not have a PointerFile and the PointerFileEntry is marked as deleted")]
        public async Task ThenFileDoesNotHaveAPointerFile(string binaryFileId)
        {
            var bfi = ((RelatedFiles)scenarioContext[binaryFileId]).Archive;
            await CheckPointerFile(bfi, shouldExist: false);
        }

        [Then("BinaryFile {word} has a PointerFile and the PointerFileEntry is marked as exists")]
        public async Task ThenBinaryFileHasAPointerFileAndThePointerFileEntryIsMarkedAsExists(string binaryFileId)
        {
            var bfi = ((RelatedFiles)scenarioContext[binaryFileId]).Archive;
            await CheckPointerFile(bfi, shouldExist: true);
        }

        [Then("the PointerFileEntry for PointerFile {word} is marked as exists")]
        public async Task ThenThePointerFileEntryForPointerFilePointerIsMarkedAsExists(string pointerFileId)
        {
            var pfi = (FileInfo)scenarioContext[pointerFileId];
            await CheckPointerFile(pfi, shouldExist: true);
        }
        //[Then("the PointerFile {word} exists")]
        //public async Task ThenThePointerFilePointerExists(string pointerFileId)
        //{
        //    var pfi = (FileInfo)scenarioContext[pointerFileId];
        //    await CheckPointerFile(pfi, shouldExist: true);
        //}

        /// <summary>
        /// if a (Binary)FileInfo -> checks whether it has a valid PointerFile
        /// if a PointerFile -> chekcs whether is it valid
        /// </summary>
        private async Task CheckPointerFile(FileInfo fi, bool shouldExist)
        {
            var (pf, pfe) = await GetPointerInfoAsync(fi);

            if (!shouldExist)
            { 
                pf.Should().BeNull();
                pfe.IsDeleted.Should().BeTrue();
            }
            else
            {
                pf.Should().NotBeNull();
                pfe.IsDeleted.Should().BeFalse();

                fi.CreationTimeUtc.Should().Be(pfe.CreationTimeUtc);
                fi.LastWriteTimeUtc.Should().Be(pfe.LastWriteTimeUtc);
            }
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

        //[Then("{int} additional PointerFile(s) exist")]
        //public void ThenPointerFilesExist(int x)
        //{
        //    var x0 = scenarioContext.GetLocalRepoStats().SkipLast(1).Last().PointerFileInfos.Length;
        //    var x1 = scenarioContext.GetLocalRepoStats().Last().PointerFileInfos.Length;

        //    Assert.AreEqual(x0 + x, x1);
        //}



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


        ///// <param name="fi"></param>
        //private async Task IsValidPointerFile(FileInfo fi)
        //{
        //    var (pf, pfe) = await GetPointerInfoAsync(fi);

        //    pf.Should().NotBeNull();

        //    pfe.Should().NotBeNull();
        //    pfe.IsDeleted.Should().BeFalse();

        //    fi.CreationTimeUtc.Should().Be(pfe.CreationTimeUtc);
        //    fi.LastWriteTimeUtc.Should().Be(pfe.LastWriteTimeUtc);
        //}

        //[Then("the PointerFile for file {word} exists")]
        //public void ThenThePointerFileForFileFileExists(string fileId)
        //{
        //    var bfi = ((RelatedFiles)scenarioContext[fileId]).Archive;
        //    IsValidPointerFile(bfi);
        //}

        private const string OLD_BINARYFILE_LOCATION = "OLD_BINARYFILE_LOCATION";
        private const string OLD_POINTERFILE = "OLD_POINTERFILE_LOCATION";

        [When("BinaryFile {word} and its PointerFile are renamed and moved to a subdirectory")]
        public async Task WhenBinaryFileFileAndItsPointerFileAreRenamedAndMovedToASubdirectory(string binaryFileId) => await MoveFiles(binaryFileId, true, true);
        [When("BinaryFile {word} is renamed and moved to a subdirectory")]
        public async Task WhenBinaryFileFileIsRenamedAndMovedToASubdirectory(string binaryFileId) => await MoveFiles(binaryFileId, true, false);
        private async Task MoveFiles(string binaryFileId, bool moveBinary, bool movePointer)
        {
            var bfi = ((RelatedFiles)scenarioContext[binaryFileId]).Archive;
            var (pf, _) = await GetPointerInfoAsync(bfi);

            scenarioContext[OLD_BINARYFILE_LOCATION] = bfi.FullName;
            scenarioContext[OLD_POINTERFILE] = pf;

            if (moveBinary)
            {
                var targetDir = bfi.Directory.CreateSubdirectory(Path.GetRandomFileName());
                var targetName = Path.GetRandomFileName();
                bfi.MoveTo(Path.Combine(targetDir.FullName, targetName)); // the FileInfo in scenarioContext is updated with this new location
            }

            if (movePointer)
            {
                File.Move(pf.FullName, bfi.FullName + Models.PointerFile.Extension);
            }
        }

        [Then("the BinaryFile at the old location no longer exist")]
        public void ThenTheBinaryFileAtTheOldLocationNoLongerExist()
        {
            File.Exists((string)scenarioContext[OLD_BINARYFILE_LOCATION]).Should().BeFalse();
        }

        [Then("the PointerFile at the old location no longer exist and the PointerFileEntry is marked as deleted")]
        public async Task ThenThePointerFileAtTheOldLocationNoLongerExistAndThePointerFileEntryIsMarkedAsDeleted()
        {
            // the pointerFile at the old location no longer exists
            var pf = (PointerFile)scenarioContext[OLD_POINTERFILE];
            File.Exists(pf.FullName).Should().BeFalse();

            // the PointerFileEntry is marked as deleted
            var pfes = await scenarioContext.GetRepository().PointerFileEntries.GetCurrentEntriesAsync(true);
            var pfe = pfes.Where(pfe => pfe.RelativeName == pf.RelativeName).Single();

            pfe.IsDeleted.Should().BeTrue();
        }

        [Then("the PointerFile at the old location exists and the PointerFileEntry is marked as exists")]
        public async Task ThenThePointerFileAtTheOldLocationExistsAndThePointerFileEntryIsMarkedAsExists()
        {
            var pf = (PointerFile)scenarioContext[OLD_POINTERFILE];
            await CheckPointerFile(new FileInfo(pf.FullName), true);
        }




        //[Then("the PointerFileEntry for BinaryFile {word} only exists at the new location")]
        //public async Task ThenBinaryFileFileOnlyExistsAtTheNewLocation(string binaryFileId)
        //{
        //    var bfi = ((RelatedFiles)scenarioContext[binaryFileId]).Archive;
        //    var (pf, pfe) = await GetPointerInfoAsync(bfi);

        //    var allPfes = (await scenarioContext.GetRepository().PointerFileEntries.GetCurrentEntriesAsync(true)).ToArray();
        //    var x = allPfes.Where(pfe0 => pfe0.BinaryHash == pfe.BinaryHash).OrderBy(pfe0 => pfe0.VersionUtc).ToArray();

        //    x.Last().RelativeName.Should().Be(pfe.RelativeName); // the last entry is the one in the local folder
        //    x.Last().IsDeleted.Should().BeFalse(); // it exists
        //    x.SkipLast(1).Select(pfe => pfe.IsDeleted).Should().AllBeEquivalentTo(true); // all other locations no longer exist
        //}

        //[Then("there is a PointerFile at the previous location of {word} with a PointerFileEntry that is marked as exists")]
        //public async Task ThenThereIsAPointerFileAtThePreviousLocationOfFileWithAPointerFileEntryThatIsMarkedAsExists(string binaryFileId)
        //{
        //    var bfi = ((RelatedFiles)scenarioContext[binaryFileId]).Archive;
        //    var (pf, pfe) = await GetPointerInfoAsync(bfi);

        //    var allPfes = (await scenarioContext.GetRepository().PointerFileEntries.GetCurrentEntriesAsync(true)).ToArray();
        //    var x = allPfes.Where(pfe0 => pfe0.BinaryHash == pfe.BinaryHash).OrderBy(pfe0 => pfe0.VersionUtc).ToArray();

        //    var previousLocationPfe = x.SkipLast(1).Last().RelativeName;

        //    x.Last().IsDeleted.Should().BeFalse(); // it exists
        //    x.SkipLast(1).Select(pfe => pfe.IsDeleted).Should().AllBeEquivalentTo(true); // all other locations no longer exist
        //}












        private FileInfo GetOrCreateSourceFile(string binaryFileId, string? name = default, int? sizeInBytes = default)
        {
            if (!scenarioContext.ContainsKey(binaryFileId))
            {
                if (name == default)
                    name = Path.GetRandomFileName();

                if (sizeInBytes == default)
                    sizeInBytes = 512000 + 1; //make it an odd size to test buffer edge cases

                var f = CreateRandomFile(Path.Combine(directories.SourceDirectory.FullName, "dir 1", name), sizeInBytes!.Value);

                scenarioContext[binaryFileId] = new RelatedFiles(f, null, null);
            }

            return ((RelatedFiles)scenarioContext[binaryFileId]).Source;
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
