using Arius.Core.Commands;
using Arius.Core.Extensions;
using Arius.Core.Repositories;
using Arius.Core.Services;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using BoDi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Text.RegularExpressions;
using System.Xml;
using TechTalk.SpecFlow;
using TechTalk.SpecFlow.Assist;

namespace Arius.Core.BehaviorTests2.StepDefinitions
{

    [Binding]
    class ArchiveSteps : TestBase
    {
        public ArchiveSteps(ScenarioContext sc) : base(sc)
        {
        }

        [Then("BinaryFile {string} has a PointerFile and the PointerFileEntry is marked as exists")]
        public async Task ThenBinaryFileHasAPointerFileAndThePointerFileEntryIsMarkedAsExists(string binaryRelativeName)
        {
            await CheckPointerFileAndPointerFileEntry(binaryRelativeName, true);
        }
        private static async Task CheckPointerFileAndPointerFileEntry(string relativeName, bool shouldExist)
        {
            var fi = FileSystem.GetFileInfo(FileSystem.ArchiveDirectory, relativeName);
            var pf = FileSystem.GetPointerFile(FileSystem.ArchiveDirectory, relativeName);
            var pfe = await Arius.GetPointerFileEntryAsync(pf.RelativeName);

            if (shouldExist)
            {
                pf.Should().NotBeNull();
                pfe.IsDeleted.Should().BeFalse();

                if (!fi.IsPointerFile())
                {
                    fi.CreationTimeUtc.Should().Be(pfe.CreationTimeUtc);
                    fi.LastWriteTimeUtc.Should().Be(pfe.LastWriteTimeUtc);
                }
            }
            else
            {
                pf.Should().BeNull();
                pfe.IsDeleted.Should().BeTrue();

                if (!fi.IsPointerFile())
                    fi.Exists.Should().BeFalse();
            }
        }

        
        [Then(@"the Chunks for BinaryFile {string} are in the {word} tier and are {word}")]
        public async Task ThenTheChunksForBinaryFileAreInTheTier(string binaryRelativeName, AccessTier tier, string hydratedStatus)
        {
            var pfe = await Arius.GetPointerFileEntryAsync(binaryRelativeName);

            var repo = Arius.GetRepository();

            var chs = await repo.Binaries.GetChunkHashesAsync(pfe.BinaryHash);

            foreach (var ch in chs)
            {
                var ch0 = repo.Chunks.GetChunkBlobByHash(ch, false);
                ch0.AccessTier.Should().Be(tier);

                var ch1 = repo.Chunks.GetChunkBlobByHash(ch, true);
                if (hydratedStatus == "HYDRATED")
                    ch1.Should().NotBeNull();
                else if (hydratedStatus == "NOT_HYDRATED")
                    ch1.Should().BeNull();
                else
                    throw new NotImplementedException();
            }
        }






        [When(@"BinaryFile {string} and its PointerFile are deleted")]
        public void BinaryFileAndPointerFileAreDeleted(string binaryRelativeName) => DeleteFiles(binaryRelativeName, true, true);

        [When(@"BinaryFile {string} is deleted")]
        public void BinaryFileIsDeleted(string binaryRelativeName) => DeleteFiles(binaryRelativeName, true, false);

        private void DeleteFiles(string binaryRelativeName, bool deleteBinaryFile, bool deletePointerFile)
        {
            var bfi = FileSystem.GetFileInfo(FileSystem.ArchiveDirectory, binaryRelativeName);

            if (deleteBinaryFile)
            {
                bfi.Delete();
            }
            if (deletePointerFile)
            {
                var pfi = FileSystem.GetPointerFile(FileSystem.ArchiveDirectory, binaryRelativeName);
                pfi.Delete();
            }

        }
    }
}
