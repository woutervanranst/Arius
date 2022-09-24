using Arius.Core.Commands;
using Arius.Core.Extensions;
using Arius.Core.Repositories;
using Arius.Core.Services;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using BoDi;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using System;
using System.Text.RegularExpressions;
using System.Xml;
using TechTalk.SpecFlow;
using TechTalk.SpecFlow.Assist;

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
            Commands.Restore.DownloadBinaryBlock.StartedHydration = false;
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
            Assert.CatchAsync<ValidationException>(async () => await Arius.RestoreCommandAsyc(synchronize: false, download: false, keepPointers: false));
        }




        [When("Copy the PointerFile of BinaryFile {string} to the restore directory")]
        public void WhenCopyThePointerFileOfToTheRestoreDirectory(string relativeBinaryFile)
        {
            FileSystem.CopyArchiveBinaryFileToRestoreDirectory(relativeBinaryFile);
        }






        //[Then("all BinaryFiles and PointerFiles are restored successfully")]
        //public void ThenAllBinaryFilesAndPointerFilesAreRestoredSuccessfully()
        //{
        //    FileSystem.RestoreDirectoryEqualToArchiveDirectory(compareBinaryFile: true, comparePointerFile: true);
        //}
        [Then("all PointerFiles are restored succesfully")]
        public void ThenAllPointerFilesAreRestoredSuccesfully()
        {
            FileSystem.RestoreDirectoryEqualToArchiveDirectory(compareBinaryFile: false, comparePointerFile: true);
        }
        [Then("all BinaryFiles are restored succesfully")]
        public void ThenAllBinaryFilesAreRestoredSuccesfully()
        {
            FileSystem.RestoreDirectoryEqualToArchiveDirectory(compareBinaryFile: true, comparePointerFile: false);
        }
        [Then("the PointerFile for BinaryFile {string} does not exist")]
        public void ThenThePointerFileForBinaryFileDoesNotExist(string relativeBinaryFile)
        {
            var d = FileSystem.RestoreDirectory;
            d.GetPointerFileInfos().Where(pfi => pfi.GetRelativeName(d).StartsWith(relativeBinaryFile)).Should().BeEmpty();
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
