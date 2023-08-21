using Arius.Core.Models;
using Arius.Core.Repositories.BlobRepository;
using Arius.Core.Services;
using Azure.Storage.Blobs.Models;
using TechTalk.SpecFlow.Assist;

namespace Arius.Core.BehaviorTests.StepDefinitions;

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
        FileSystem.CreateBinaryFileIfNotExists(binaryRelativeName, size);
    }
    
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



    [When("deduplicated and archived to the {word} tier")]
    public async Task WhenABinaryFileOfSizeIsDeduplicatedAndArchivedToTheCoolTier(AccessTier tier)
    {
        await TestSetup.ArchiveCommandAsync(tier, dedup: true);
    }
    [When(@"archived to the {word} tier with option RemoveLocal")]
    public async Task WhenALocalFileOfSizeIsArchivedToTierWithOptionRemoveLocal(AccessTier tier)
    {
        await TestSetup.ArchiveCommandAsync(tier, removeLocal: true);
    }
    [When("archived to the {word} tier")]
    public async Task WhenArchivedToTheTier(AccessTier tier)
    {
        await TestSetup.ArchiveCommandAsync(tier);
    }

    [When(@"the following BinaryFiles are archived to {word} tier:")]
    public async Task GivenTheFollowingLocalFilesAreArchivedToTier(AccessTier tier, Table table)
    {
        var files = table.CreateSet<FileTableEntry>().ToList();

        foreach (var f in files)
        {
            if (!string.IsNullOrWhiteSpace(f.Size) && string.IsNullOrWhiteSpace(f.SourceRelativeName))
            {
                // Create a new file
                FileSystem.CreateBinaryFileIfNotExists(f.RelativeName, f.Size);
            }
            else if (string.IsNullOrWhiteSpace(f.Size) && !string.IsNullOrWhiteSpace(f.SourceRelativeName))
            {
                // Duplicate a file
                FileSystem.DuplicateBinaryFile(f.RelativeName, f.SourceRelativeName);
            }
            else
                throw new ArgumentException();
        }

        await TestSetup.ArchiveCommandAsync(tier);
    }
    private record FileTableEntry(string RelativeName, string Size, string SourceRelativeName);

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


    
    [Then("{int} additional Chunk(s) and Binary/Binaries")]
    public void ThenAdditionalChunksAndManifests(int x)
    {
        ThenAdditionalChunksAndManifests(x.ToString(), x.ToString());
        //var rs0 = TestSetup.Stats.SkipLast(1).Last();
        //var rs1 = TestSetup.Stats.Last();

        //(rs0.ChunkEntryCount + addtlChunks).Should().Be(rs1.ChunkEntryCount);
        //(rs0.BinaryCount + addtlChunks).Should().Be(rs1.BinaryCount);
    }
    [Then("{string} additional Chunk(s) and {string} additional Binary/Binaries")]
    public void ThenAdditionalChunksAndManifests(string addtlChunksStr, string addtlBinariesStr)
    {
        var rs0 = TestSetup.Stats.SkipLast(1).Last();
        var rs1 = TestSetup.Stats.Last();

        if (int.TryParse(addtlChunksStr, out int addtlChunks))
            (rs0.ChunkEntryCount + addtlChunks).Should().Be(rs1.ChunkEntryCount);
        else if (addtlChunksStr == "MORE_THAN_ONE")
            (rs0.ChunkEntryCount + 1).Should().BeLessThan(rs1.ChunkEntryCount);
        else
            throw new NotImplementedException();

        if (int.TryParse(addtlBinariesStr, out int addtlBinaries))
            (rs0.BinaryCount + addtlBinaries).Should().Be(rs1.BinaryCount);
        else
            throw new NotImplementedException();
    }
    //public void ThenAdditionalChunksAndManifests(int addtlChunks, int addtlBinaries)
    //{
    //    var rs0 = TestSetup.Stats.SkipLast(1).Last();
    //    var rs1 = TestSetup.Stats.Last();

    //    (rs0.ChunkEntryCount + addtlChunks).Should().Be(rs1.ChunkEntryCount);
    //    (rs0.BinaryCount + addtlBinaries).Should().Be(rs1.BinaryCount);
    //}


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
        var fi  = FileSystem.GetFileInfo(FileSystem.ArchiveDirectory, relativeName);
        var pf  = FileSystem.GetPointerFile(FileSystem.ArchiveDirectory, relativeName);
        var pfe = await TestSetup.GetPointerFileEntryAsync(relativeName);
        
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

    [Then(@"the Chunk for BinaryFile {string} are in the {word} tier and are {word} and have OriginalLength {word}")]
    public async Task ThenTheChunkForBinaryFileAreInTheTier(string binaryRelativeName, AccessTier tier, string hydratedStatus, string sizeStr)
    {
        var pfe = await TestSetup.GetPointerFileEntryAsync(binaryRelativeName);

        var ce = await Repository.GetChunkEntryAsync(pfe.BinaryHash);

        var chs = await Repository.GetChunkListAsync(pfe.BinaryHash).ToArrayAsync();
        chs.Length.Should().Be(ce.ChunkCount);

        var size = FileSystem.SizeInBytes(sizeStr);

        foreach (var ch in chs)
        {
            // Check the ChunkEntries
            ce.AccessTier.Should().Be(tier);
            ce.OriginalLength.Should().Be(size);
            ce.ArchivedLength.Should().BeGreaterThan(0);
            ce.ChunkCount.Should().Be(1); // not chunked
            ce.IncrementalLength.Should().BeGreaterThan(0);

            // Check the actual Blob
            var b = TestSetup.GetBlobClient(BlobContainer.CHUNKS_FOLDER_NAME, ch);
            var p = (await b.GetPropertiesAsync()).Value;

            ce.AccessTier.Should().BeEquivalentTo((AccessTier)p.AccessTier);
            ce.OriginalLength.ToString().Should().Be(p.Metadata[Blob.ORIGINAL_CONTENT_LENGTH_METADATA_KEY]);
            ce.ArchivedLength.Should().Be(p.ContentLength);
            
            p.ContentType.Should().Be(CryptoService.ContentType);

            // Hydrated status
            var hb = await Repository.GetHydratedChunkBlobAsync(ch);
            if (hydratedStatus == "HYDRATED")
            {
                hb.Should().NotBeNull();
                ce.AccessTier.Should().NotBe(AccessTier.Archive);
            }
            else if (hydratedStatus == "NOT_HYDRATED")
            {
                hb.Should().BeNull();
                ce.AccessTier.Should().Be(AccessTier.Archive);
            }
            else
                throw new NotImplementedException();
        }
    }
}