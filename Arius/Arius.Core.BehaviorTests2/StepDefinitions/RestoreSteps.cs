using Arius.Core.BehaviorTests2.Extensions;
using Arius.Core.Extensions;
using Arius.Core.Models;
using Azure.Storage.Blobs.Models;

namespace Arius.Core.BehaviorTests2.StepDefinitions
{

    [Binding]
    class RestoreSteps : TestBase
    {
        public RestoreSteps(ScenarioContext sc) : base(sc)
        {
        }

        [BeforeScenario]
        public void Reset()
        {
            Commands.Restore.DownloadBinaryBlock.RestoredFromOnlineTier = false;
            Commands.Restore.DownloadBinaryBlock.RestoredFromLocal = false;
        }

        [Given("a clean restore directory")]
        public void GivenACleanRestoreDirectory()
        {
            FileSystem.RestoreDirectory.Clear();
        }


        [When("restore --synchronize --download --keepPointers")]
        public async Task WhenRestore_Synchronize_Download_KeepPointers()
        {
            await Arius.RestoreCommandAsyc(synchronize: true, download: true, keepPointers: true);
        }
        [When("restore --synchronize --keepPointers")]
        public async Task WhenRestore_Synchronize_KeepPointers()
        {
            await Arius.RestoreCommandAsyc(synchronize: true, download: false, keepPointers: true);
        }
        [When("restore --synchronize --download")]
        public async Task WhenRestore_Synchronize_Download()
        {
            await Arius.RestoreCommandAsyc(synchronize: true, download: true, keepPointers: false);
        }
        [When("restore --synchronize")]
        public async Task WhenRestore_Synchronize()
        {
            await Arius.RestoreCommandAsyc(synchronize: true, download: false, keepPointers: false);
        }
        [When("restore --download")]
        public async Task WhenRestore_Download()
        {
            await Arius.RestoreCommandAsyc(synchronize: false, download: true, keepPointers: false);
        }
        [When("restore --download --keepPointers")]
        public async Task WhenRestore_Download_KeepPointers()
        {
            await Arius.RestoreCommandAsyc(synchronize: false, download: true, keepPointers: true);
        }
        [When("restore expect a ValidationException")]
        public async Task WhenRestore()
        {
            Func<Task> t = () => Arius.RestoreCommandAsyc(synchronize: false, download: false, keepPointers: false);
            await t.Should().ThrowAsync<FluentValidation.ValidationException>();

            //Assert.CatchAsync<FluentValidation.ValidationException>(async () => await Arius.RestoreCommandAsyc(synchronize: false, download: false, keepPointers: false));
        }




        [When("copy the PointerFile of BinaryFile {string} to the restore directory")]
        public void WhenCopyThePointerFileOfToTheRestoreDirectory(string relativeBinaryFile)
        {
            FileSystem.CopyArchiveBinaryFileToRestoreDirectory(relativeBinaryFile);
        }






        [Then("all PointerFiles are restored successfully")]
        public void ThenAllPointerFilesAreRestoredsuccessfully()
        {
            FileSystem.RestoreDirectoryEqualToArchiveDirectory(compareBinaryFile: false, comparePointerFile: true);
        }
        [Then("all BinaryFiles are restored successfully")]
        public void ThenAllBinaryFilesAreRestoredsuccessfully()
        {
            FileSystem.RestoreDirectoryEqualToArchiveDirectory(compareBinaryFile: true, comparePointerFile: false);
        }
        [Then("the PointerFile for BinaryFile {string} does not exist")]
        public void ThenThePointerFileForBinaryFileDoesNotExist(string relativeBinaryFile)
        {
            var d = FileSystem.RestoreDirectory;
            d.GetPointerFileInfos().Where(pfi => pfi.GetRelativeName(d).StartsWith(relativeBinaryFile)).Should().BeEmpty();
        }
        [Then("the BinaryFile {string} exists")]
        public void ThenTheBinaryFileExists(string relativeBinaryFile)
        {
            var d = FileSystem.RestoreDirectory;
            d.GetBinaryFileInfos().Should().Contain(pfi => pfi.GetRelativeName(d).Equals(relativeBinaryFile));
        }
        [Then("the BinaryFile {string} does not exist")]
        public void ThenTheBinaryFileDoesNotExist(string relativeBinaryFile)
        {
            var d = FileSystem.RestoreDirectory;
            d.GetBinaryFileInfos().Should().NotContain(pfi => pfi.GetRelativeName(d).Equals(relativeBinaryFile));
        }
        [Then("no PointerFiles are present")]
        public void ThenNoPointerFilesArePresent()
        {
            FileSystem.RestoreDirectory.GetPointerFileInfos().Should().BeEmpty();
        }
        [Then("no BinaryFiles are present")]
        public void ThenNoBinaryFilesArePresent()
        {
            FileSystem.RestoreDirectory.GetBinaryFileInfos().Should().BeEmpty();
        }
        [Then("only the PointerFile for BinaryFile {string} is present")]
        public void ThenOnlyThePointerFileForBinaryFileIsPresent(string relativeBinaryFile)
        {
            var d = FileSystem.RestoreDirectory;
            d.GetPointerFileInfos().Single().GetRelativeName(d).Should().StartWith(relativeBinaryFile);
        }
        [Then("only the BinaryFile {string} is present")]
        public void ThenOnlyTheBinaryFileIsPresent(string relativeBinaryFile)
        {
            var d = FileSystem.RestoreDirectory;
            d.GetBinaryFileInfos().Single().GetRelativeName(d).Should().BeEquivalentTo(relativeBinaryFile);
        }
        [Then("the BinaryFile {string} is restored from online tier")]
        public void ThenTheBinaryFileIsRestored(string relativeBinaryFile)
        {
            FileSystem.RestoreBinaryFileEqualToArchiveBinaryFile(relativeBinaryFile);
            Commands.Restore.DownloadBinaryBlock.RestoredFromOnlineTier.Should().BeTrue();
        }
        [Then("the BinaryFile {string} is restored from local")]
        public void ThenTheBinaryFileIsRestoredFromLocal(string relativeBinaryFile)
        {
            FileSystem.RestoreBinaryFileEqualToArchiveBinaryFile(relativeBinaryFile);
            Commands.Restore.DownloadBinaryBlock.RestoredFromLocal.Should().BeTrue();
        }






        [Then("the hydration for the chunks of BinaryFile {string} have started")]
        public async Task ThenTheHydrationForTheChunksOfBinaryFileHaveStarted(string relativeBinaryFile)
        {
            var pf = FileSystem.GetPointerFile(FileSystem.RestoreDirectory, relativeBinaryFile);
            var ch = new ChunkHash(pf.Hash); // hack

            var e = await Arius.RehydrateChunkExists(ch);
            e.Should().BeTrue();
        }
        [When("the chunk of BinaryFile {string} is copied to the rehydrate folder and the original chunk is moved to the {word} tier")]
        public async Task WhenTheChunkOfBinaryFileIsCopiedToTheRehydrateFolderAndTheOriginalChunkIsMovedToTheArchiveTier(string relativeBinaryFile, AccessTier tier)
        {
            tier.Should().Be(AccessTier.Archive);

            var pf = FileSystem.GetPointerFile(FileSystem.RestoreDirectory, relativeBinaryFile);
            var ch = new ChunkHash(pf.Hash);

            await Arius.CopyChunkToRehydrateFolderAndArchiveOriginal(ch);
        }
        [Then("the rehydrate folder does not exist")]
        public async void ThenTheRehydrateFolderDoesNotExist()
        {
            var e = await Arius.RehydrateFolderExists();
            e.Should().BeFalse();
        }






        //[Then("the restore directory is empty")]
        //public void ThenTheRestoreDirectoryIsEmpty()
        //{
        //    FileSystem.RestoreDirectory.GetFiles().Should().BeEmpty();
        //}




        [Given("a random PointerFile for BinaryFile {string}")]
        public void GivenARandomPointerFileForBinaryFile(string relativeBinaryFile)
        {
            // Take a real PointerFile
            var pfi = FileSystem.ArchiveDirectory.GetPointerFileInfos().First();
            // Build the target filename
            var pfn = Path.Combine(FileSystem.RestoreDirectory.FullName, relativeBinaryFile + Models.PointerFile.Extension);

            pfi.CopyTo(pfn);
        }

        [Given("a random BinaryFile {string}")]
        public void GivenARandomBinaryFile(string relativeBinaryFile)
        {
            var bfn = Path.Combine(FileSystem.RestoreDirectory.FullName, relativeBinaryFile);

            File.WriteAllText(bfn, "some random binary stuff");
        }











        //[Then("the PointerFile for BinaryFile {string} is not present")]
        //public void ThenThePointerFileForBinaryFileIsNotPresent(string relativeBinaryFile)
        //{
        //    FileSystem.GetPointerFile(FileSystem.RestoreDirectory, relativeBinaryFile).Should().BeNull();
        //}










    }
}
