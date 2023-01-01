using Azure.Storage.Blobs.Models;
using TechTalk.SpecFlow.Assist;

namespace Arius.Core.BehaviorTests.StepDefinitions
{
    [Binding]
    class ArchiveSteps : TestBase
    {
        public ArchiveSteps(ScenarioContext sc) : base(sc)
        {
        }

        
        [StepArgumentTransformation]
        public static AccessTier TierTransform(string tier) => (AccessTier)tier;


        [Given("a clean archive directory")]
        public void GivenACleanRestoreDirectory()
        {
            FileSystem.ArchiveDirectory.Clear();
        }

        [Given(@"a BinaryFile {string} of size {string}")]
        public void GivenABinaryFileOfSize(string binaryRelativeName, string size)
        {
            FileSystem.CreateBinaryFile(binaryRelativeName, size);
        }

        [Given(@"a BinaryFile {string} of size {string} is archived to the {word} tier")]
        public async Task GivenALocalFileOfSizeIsArchivedToTier(string binaryRelativeName, string size, AccessTier tier)
        {
            FileSystem.CreateBinaryFile(binaryRelativeName, size);

            await Arius.ArchiveCommandAsync(tier);
        }

        [Given(@"a BinaryFile {string} of size {string} is archived to the {word} tier with option RemoveLocal")]
        public async Task GivenALocalFileOfSizeIsArchivedToTierWithOptionRemoveLocal(string binaryRelativeName, string size, AccessTier tier)
        {
            FileSystem.CreateBinaryFile(binaryRelativeName, size);

            await Arius.ArchiveCommandAsync(tier, removeLocal: true);
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
                    FileSystem.CreateBinaryFile(f.RelativeName, f.Size);
                }
                else if (string.IsNullOrWhiteSpace(f.Size) && !string.IsNullOrWhiteSpace(f.SourceRelativeName))
                {
                    // Duplicate a file
                    FileSystem.DuplicateBinaryFile(f.RelativeName, f.SourceRelativeName);
                }
                else
                    throw new ArgumentException();
            }

            await Arius.ArchiveCommandAsync(tier);
        }
        record FileTableEntry(string RelativeName, string Size, string SourceRelativeName);

        [Given(@"a BinaryFile {word} duplicate of BinaryFile {word}")]
        public void GivenABinaryFileDuplicateOfBinaryFile(string binaryRelativeName, string sourceBinaryRelativeName)
        {
            FileSystem.DuplicateBinaryFile(binaryRelativeName, sourceBinaryRelativeName);
        }

        [Given(@"a Pointer of BinaryFile {word} duplicate of the Pointer of BinaryFile {word}")]
        private static void GivenAPointerFileDuplicateOfThePointerOfBinaryFile(string relativeBinaryName, string sourceRelativeBinaryName)
        {
            FileSystem.DuplicatePointerFile(relativeBinaryName, sourceRelativeBinaryName);
        }


        [When("archived to the {word} tier")]
        public async Task WhenArchivedToTheTier(AccessTier tier)
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
        [When("BinaryFile {string} is undeleted")]
        public void WhenBinaryFileIsUndeleted(string binaryRelativeName)
        {
            UndeleteFile(binaryRelativeName);
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
        private void UndeleteFile(string binaryRelativeName)
        {
            var bfi = (FileInfo)scenarioContext[binaryRelativeName];
            bfi.CopyTo(FileSystem.TempDirectory, FileSystem.ArchiveDirectory, true);
            bfi.Delete();
        }

        [When("BinaryFile {string} is moved to {string}")]
        public void WhenBinaryFileIsMovedTo(string sourceRelativeBinaryName, string targetRelativeBinaryName)
        {
            FileSystem.Move(sourceRelativeBinaryName, targetRelativeBinaryName, moveBinary: true, movePointer: false);
        }
        [When("BinaryFile {string} and its PointerFile are moved to {string}")]
        public void WhenBinaryFileAndItsPointerFileAreMovedTo(string sourceRelativeBinaryName, string targetRelativeBinaryName)
        {
            FileSystem.Move(sourceRelativeBinaryName, targetRelativeBinaryName, moveBinary: true, movePointer: true);
        }
        [When("the PointerFile for BinaryFile {string} is moved to {string}")]
        public void WhenThePointerFileForBinaryFileIsMovedTo(string sourceRelativeBinaryName, string targetRelativeBinaryName)
        {
            FileSystem.Move(sourceRelativeBinaryName, targetRelativeBinaryName, moveBinary: false, movePointer: true);
        }

        
        [Then("{int} additional Chunk(s) and Manifest(s)")]
        public void ThenAdditionalChunksAndManifests(int x)
        {
            var rs0 = Arius.Stats.SkipLast(1).Last();
            var rs1 = Arius.Stats.Last();

            (rs0.ChunkCount + x).Should().Be(rs1.ChunkCount);
            (rs0.BinaryCount + x).Should().Be(rs1.BinaryCount);
        }

        [Then("BinaryFile {string} no longer exists")]
        public void ThenBinaryFileFileNoLongerExists(string binaryRelativeName)
        {
            var fi = FileSystem.GetFileInfo(FileSystem.ArchiveDirectory, binaryRelativeName);
            fi.Exists.Should().BeFalse();
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
        [Then(@"a PointerFileEntry for a BinaryFile {string} is marked as exists")]
        public async Task ThenThePointerFileEntryForAPointerOfBinaryFileIsMarkedAsExists(string binaryRelativeName)
        {
            await CheckPointerFileAndPointerFileEntry(binaryRelativeName, shouldExist: true);
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="relativeName">Relative Name of the PointerFile or BinaryFile</param>
        /// <param name="shouldExist">Whether the PointerFile and PointerFileEntry should exist</param>
        /// <returns></returns>
        private static async Task CheckPointerFileAndPointerFileEntry(string relativeName, bool shouldExist)
        {
            var fi = FileSystem.GetFileInfo(FileSystem.ArchiveDirectory, relativeName);
            var pf = FileSystem.GetPointerFile(FileSystem.ArchiveDirectory, relativeName);
            var pfe = await Arius.GetPointerFileEntryAsync(relativeName);

            if (shouldExist)
            {
                pf.Should().NotBeNull();
                pfe.IsDeleted.Should().BeFalse();

                if (fi.Exists && !fi.IsPointerFile())
                {
                    // Check that the timestamps match the BinaryFile
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
