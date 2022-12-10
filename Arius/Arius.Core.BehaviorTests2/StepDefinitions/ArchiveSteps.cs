using Arius.Core.Commands;
using Arius.Core.Extensions;
using Arius.Core.Models;
using Arius.Core.Repositories;
using Arius.Core.Services;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using BoDi;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
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

        
        [StepArgumentTransformation]
        public static AccessTier TierTransform(string tier) => (AccessTier)tier;


        [Given(@"a BinaryFile {string} of size {string} is archived to the {word} tier")]
        public async Task GivenALocalFileOfSizeIsArchivedTo(string binaryFileName, string size, AccessTier tier)
        {
            CreateFile(binaryFileName, size);

            await Arius.ArchiveCommandAsync(tier);
        }
        
        [Given(@"the following BinaryFiles are archived to {word} tier:")]
        public async Task GivenTheFollowingLocalFilesAreArchivedToTier(AccessTier tier, Table table)
        {
            var files = table.CreateSet<FileTableEntry>().ToList();

            foreach (var f in files)
            {
                if (!string.IsNullOrWhiteSpace(f.Size) && string.IsNullOrWhiteSpace(f.SourceRelativeName))
                {
                    // Create a new file
                    CreateFile(f.RelativeName, f.Size);
                }
                else if (string.IsNullOrWhiteSpace(f.Size) && !string.IsNullOrWhiteSpace(f.SourceRelativeName))
                {
                    // Duplicate a file
                    DuplicateFile(f.RelativeName, f.SourceRelativeName);
                }
                else
                    throw new ArgumentException();
            }

            await Arius.ArchiveCommandAsync(tier);
        }
        record FileTableEntry(string RelativeName, string Size, string SourceRelativeName);
        private static void CreateFile(string relativeName, string size)
        {
            var sizeInBytes = size switch
            {
                "BELOW_ARCHIVE_TIER_LIMIT"
                    => 12 * 1024 + 1, // 12 KB
                "ABOVE_ARCHIVE_TIER_LIMIT"
                    => 1024 * 1024 + 1, // Note: the file needs to be big enough (> 1 MB) to put into Archive storage (see ChunkBlobBase.SetAccessTierPerPolicyAsync)
                _ when
                    // see https://stackoverflow.com/a/3513858
                    // see https://codereview.stackexchange.com/a/67506
                    int.TryParse(Regex.Match(size, @"(?<size>\d*) KB").Groups["size"].Value, out var size0)
                    => size0,
                _ =>
                    throw new ArgumentOutOfRangeException()
            };

            if (FileSystem.Exists(FileSystem.ArchiveDirectory, relativeName))
            {
                if (FileSystem.Length(FileSystem.ArchiveDirectory, relativeName) != sizeInBytes)
                    throw new ArgumentException("File already exists and is of different size");
                
                // Reuse the file that already exists
                return;
            }
            else
                FileSystem.CreateFile(relativeName, sizeInBytes);
        }
        private static void DuplicateFile(string relativeName, string sourceRelativeName)
        {
            if (FileSystem.GetFileInfo(FileSystem.ArchiveDirectory, relativeName).Exists)
                return;

            var bfi = FileSystem.GetFileInfo(FileSystem.ArchiveDirectory, sourceRelativeName);
            bfi.CopyTo(Path.Combine(FileSystem.ArchiveDirectory.FullName, relativeName));
        }

        
        [When("archived to the {word} tier")]
        public async Task WhenArchivedToTheCoolTier(AccessTier tier)
        {
            await Arius.ArchiveCommandAsync(tier);
        }

        [When(@"BinaryFile {string} and its PointerFile are deleted")]
        public void BinaryFileAndPointerFileAreDeleted(string binaryRelativeName)
        {
            DeleteFiles(binaryRelativeName, deleteBinaryFile: true, deletePointerFile: true);
        }

        [When(@"BinaryFile {string} is deleted")]
        public void BinaryFileIsDeleted(string binaryRelativeName)
        {
            DeleteFiles(binaryRelativeName, deleteBinaryFile: true, deletePointerFile: false);
        }
        private void DeleteFiles(string binaryRelativeName, bool deleteBinaryFile, bool deletePointerFile)
        {
            var bfi = FileSystem.GetFileInfo(FileSystem.ArchiveDirectory, binaryRelativeName);

            if (deleteBinaryFile)
            {
                // Store for future undelete
                var bfi2 = bfi.CopyTo(FileSystem.ArchiveDirectory, FileSystem.TempDirectory, true);
                scenarioContext[binaryRelativeName] = bfi2;

                bfi.Delete();
            }
            if (deletePointerFile)
            {
                var pfi = FileSystem.GetPointerFile(FileSystem.ArchiveDirectory, binaryRelativeName);
                pfi.Delete();
            }
        }

        [When("BinaryFile {string} is undeleted")]
        public void WhenBinaryFileIsUndeleted(string binaryRelativeName)
        {
            var bfi = (FileInfo)scenarioContext[binaryRelativeName];
            bfi.CopyTo(FileSystem.TempDirectory, FileSystem.ArchiveDirectory, true);
            bfi.Delete();
        }

        
        [Then("{int} additional Chunk(s) and Manifest(s)")]
        public void ThenAdditionalChunksAndManifests(int x)
        {
            var rs0 = Arius.Stats.SkipLast(1).Last();
            var rs1 = Arius.Stats.Last();

            (rs0.ChunkCount + x).Should().Be(rs1.ChunkCount);
            (rs0.BinaryCount + x).Should().Be(rs1.BinaryCount);
        }

        [Then("BinaryFile {string} has a PointerFile and the PointerFileEntry is marked as exists")]
        public async Task ThenBinaryFileHasAPointerFileAndThePointerFileEntryIsMarkedAsExists(string binaryRelativeName)
        {
            await CheckPointerFileAndPointerFileEntry(binaryRelativeName, shouldExist: true);
        }
        [Then("the PointerFileEntry for BinaryFile {string} is marked as deleted")]
        public async Task ThenThePointerFileEntryForIsMarkedAsDeleted(string binaryRelativeName)
        {
            await CheckPointerFileAndPointerFileEntry(binaryRelativeName, shouldExist: false);
        }
        private static async Task CheckPointerFileAndPointerFileEntry(string relativeName, bool shouldExist)
        {
            var fi = FileSystem.GetFileInfo(FileSystem.ArchiveDirectory, relativeName);
            var pf = FileSystem.GetPointerFile(FileSystem.ArchiveDirectory, relativeName);
            var pfe = await Arius.GetPointerFileEntryAsync(relativeName);

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
    }
}
