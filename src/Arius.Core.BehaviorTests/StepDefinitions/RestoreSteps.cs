using System.Diagnostics;
using Arius.Core.Models;
using Azure.Storage.Blobs.Models;

namespace Arius.Core.BehaviorTests.StepDefinitions;

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
        await TestSetup.RestoreCommandAsyc(synchronize: true, download: true, keepPointers: true);
    }
    [When("restore --synchronize --keepPointers")]
    public async Task WhenRestore_Synchronize_KeepPointers()
    {
        await TestSetup.RestoreCommandAsyc(synchronize: true, download: false, keepPointers: true);
    }
    [When("restore --synchronize --download")]
    public async Task WhenRestore_Synchronize_Download()
    {
        await TestSetup.RestoreCommandAsyc(synchronize: true, download: true, keepPointers: false);
    }
    [When("restore --synchronize")]
    public async Task WhenRestore_Synchronize()
    {
        await TestSetup.RestoreCommandAsyc(synchronize: true, download: false, keepPointers: false);
    }
    [When("restore --download")]
    public async Task WhenRestore_Download()
    {
        await TestSetup.RestoreCommandAsyc(synchronize: false, download: true, keepPointers: false);
    }
    [When("restore --download --keepPointers")]
    public async Task WhenRestore_Download_KeepPointers()
    {
        await TestSetup.RestoreCommandAsyc(synchronize: false, download: true, keepPointers: true);
    }
    [When("restore relativename {string}")]
    public async Task WhenRestoreRelativename(RelativePath relativeName)
    {
        await TestSetup.RestoreCommandAsync(relativeName);
    }

    [When("restore expect a ValidationException")]
    public async Task WhenRestoreValidationException()
    {
        Func<Task> t = () => TestSetup.RestoreCommandAsyc(synchronize: false, download: false, keepPointers: false);
        await t.Should().ThrowAsync<ArgumentException>();

        //Assert.CatchAsync<FluentValidation.ValidationException>(async () => await Arius.RestoreCommandAsyc(synchronize: false, download: false, keepPointers: false));
    }
    [When("restore successful")]
    public async Task RestoreSuccessful()
    {
        // this just runs without any exception
        var r = await TestSetup.RestoreCommandAsyc(synchronize: false, download: false, keepPointers: false);
        r.Should().Be(0);
    }

    [When("copy the PointerFile of BinaryFile {string} to the restore directory")]
    public void WhenCopyThePointerFileOfToTheRestoreDirectory(RelativePath relativeBinaryFile)
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
    public void ThenThePointerFileForBinaryFileDoesNotExist(RelativePath relativeBinaryFile)
    {
        var d = FileSystem.RestoreDirectory;
        d.GetPointerFileInfos().Where(pfi => pfi.GetRelativeName(d).StartsWith(relativeBinaryFile)).Should().BeEmpty();
    }

    [Then("the BinaryFile {string} exists")]
    public void ThenTheBinaryFileExists(RelativePath relativeBinaryFile)
    {
        var d = FileSystem.RestoreDirectory;
        d.GetBinaryFileInfos().Should().Contain(pfi => pfi.GetRelativeName(d).Equals(relativeBinaryFile));
    }
    [Then("the BinaryFile {string} does not exist")]
    public void ThenTheBinaryFileDoesNotExist(RelativePath relativeBinaryFile)
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
    public void ThenOnlyThePointerFileForBinaryFileIsPresent(RelativePath relativeBinaryFile)
    {
        var d = FileSystem.RestoreDirectory;
        d.GetPointerFileInfos().Single().GetRelativeName(d).Should().StartWith(relativeBinaryFile);
    }

    [Then("only the BinaryFile {string} is present")]
    public void ThenOnlyTheBinaryFileIsPresent(RelativePath relativeBinaryFile)
    {
        var d = FileSystem.RestoreDirectory;
        d.GetBinaryFileInfos().Single().GetRelativeName(d).Should().BeEquivalentTo(relativeBinaryFile);
    }

    [Then("the BinaryFile {string} is restored from online tier")]
    public void ThenTheBinaryFileIsRestored(RelativePath relativeBinaryFile)
    {
        FileSystem.RestoreBinaryFileEqualToArchiveBinaryFile(relativeBinaryFile);
        Commands.Restore.DownloadBinaryBlock.RestoredFromOnlineTier.Should().BeTrue();
    }
    [Then("the BinaryFile {string} is restored from local")]
    public void ThenTheBinaryFileIsRestoredFromLocal(RelativePath relativeBinaryFile)
    {
        FileSystem.RestoreBinaryFileEqualToArchiveBinaryFile(relativeBinaryFile);
        Commands.Restore.DownloadBinaryBlock.RestoredFromLocal.Should().BeTrue();
    }

    [Then("the hydration for the chunks of BinaryFile {string} have started")]
    public async Task ThenTheHydrationForTheChunksOfBinaryFileHaveStarted(RelativePath relativeBinaryFile)
    {
        var pf = FileSystem.GetPointerFile(FileSystem.RestoreDirectory, relativeBinaryFile);
        var ch = (ChunkHash)pf.BinaryHash; // hack

        var e = await TestSetup.RehydrateChunkExists(ch);
        e.Should().BeTrue();
    }
        
        
    [When("the chunk of BinaryFile {string} is copied to the rehydrate folder and the original chunk is moved to the {word} tier")]
    public async Task WhenTheChunkOfBinaryFileIsCopiedToTheRehydrateFolderAndTheOriginalChunkIsMovedToTheArchiveTier(RelativePath relativeBinaryFile, AccessTier tier)
    {
        tier.Should().Be(AccessTier.Archive); // other tiers are not supported in this step

        var pf = FileSystem.GetPointerFile(FileSystem.RestoreDirectory, relativeBinaryFile);
        var ch = (ChunkHash)pf.BinaryHash;

        await TestSetup.CopyChunkToRehydrateFolderAndArchiveOriginal(ch);
    }
    [Then("the rehydrate folder does not exist")]
    public async Task ThenTheRehydrateFolderDoesNotExist()
    {
        var e = await TestSetup.RehydrateFolderExists();
        e.Should().BeFalse();
    }
}