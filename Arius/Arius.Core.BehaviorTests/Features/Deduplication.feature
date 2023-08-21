Feature: Deduplication

TODO / Backlog
chunk1, 2, 3 are already uploaded. file 2 = chunk 2,3. archive.

@todo
Scenario: Archive_Directory_Dedup_Success
#    await TestSetup.PurgeRemote(); //purge the remote in case non-deduped files exist
#
#    RepoStats(out var _, out var chunkBlobItemCount0, out var binaryCount0, out var currentPfeWithDeleted0, out var currentPfeWithoutDeleted0, out var allPfes0);
#
#    // Archive some files
#    TestSetup.StageArchiveTestDirectory(out var bfi1, TestSetup.SourceFilesType.File1);
#    TestSetup.StageArchiveTestDirectory(out var bfi2, TestSetup.SourceFilesType.File2);
#    TestSetup.StageArchiveTestDirectory(out var bfi4, TestSetup.SourceFilesType.File4WithSpace);
#    await ArchiveCommand(dedup: true);
#
#    // Archive a file, which consists out of File1 + File2
#    TestSetup.StageArchiveTestDirectory(out var bfi_deduped, TestSetup.SourceFilesType.File5Deduplicated);
#    await ArchiveCommand(dedup: true);
#
#    RepoStats(out var repo, out var chunkBlobItemCount1, out var binaryCount1, out var currentPfeWithDeleted1, out var currentPfeWithoutDeleted1, out var allPfes1);
#
#    GetPointerInfo(repo, bfi1, out var pf0, out var pfe0);
#    var ch0 = await repo.Binaries.GetChunkHashesAsync(pf0.Hash);
#
#    GetPointerInfo(repo, bfi2, out var pf1, out var pfe1);
#    var ch1 = await repo.Binaries.GetChunkHashesAsync(pf1.Hash);
#
#    GetPointerInfo(repo, bfi_deduped, out var pf5, out var pfe5);
#    var ch5 = await repo.Binaries.GetChunkHashesAsync(pf5.Hash);
#    var ch5_UniqueChunks = ch5.Except(ch0).Except(ch1).ToArray();
#
#    // Validate that there are very few unique bytes
#    var percentageUnique = (double)ch5_UniqueChunks.Length / (ch0.Length + ch1.Length);
#    Assert.IsTrue(percentageUnique < 0.03, percentageUnique.ToString()); //0.03 value established empirically
#        
#    // There should only be one unique chunk (ok maybe two but this hasnt happened yet)
#    Assert.AreEqual(1, ch5_UniqueChunks.Length);
#
#    // The IncrementalLength is equal to the only unique chunk length
#    var incrementalLength = (await repo.Binaries.GetPropertiesAsync(pf5.Hash)).IncrementalLength;
#    var uniqueChunkSize = ch5_UniqueChunks.Select(ch => repo.Chunks.GetChunkBlobByHash(ch, false)).Sum(c => c.Length);
#    Assert.AreEqual(incrementalLength, uniqueChunkSize);

@todo
Scenario: Restore_DedupedFile_Success
#        TestSetup.StageArchiveTestDirectory(out FileInfo _);
#        await ArchiveCommand(dedup: true);
#
#        await RestoreCommand(RestoreTestDirectory.FullName, true, true, true);
#
#        // the BinaryFile is restored
#        var ps = GetPointerService();
#        var r_pfi = RestoreTestDirectory.GetPointerFileInfos().Single();
#        var r_pf = ps.GetPointerFile(RestoreTestDirectory, r_pfi);
#        var r_bfi = ps.GetBinaryFile(r_pf, ensureCorrectHash: true);
#        Assert.IsNotNull(r_bfi);

@todo
Scenario: Restore_DedupedDirectory_Success
#        TestSetup.StageArchiveTestDirectory(out FileInfo[] _);
#        await ArchiveCommand(purgeRemote: true, dedup: true, removeLocal: false);
#
#        // the restore directory is empty
#        Assert.IsFalse(RestoreTestDirectory.EnumerateFiles().Any());
#
#        await RestoreCommand(RestoreTestDirectory.FullName, true, true);
#
#        Assert.IsTrue(RestoreTestDirectory.EnumerateFiles().Any());
#
#        // TODO add actual tests
#        throw new NotImplementedException();
