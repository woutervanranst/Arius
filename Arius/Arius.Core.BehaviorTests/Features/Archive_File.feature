Feature: Archive File

Link to a feature: [Calculator](Arius.Core.BehaviorTests/Features/Calculator.feature)
***Further read***: **[Learn more about how to generate Living Documentation](https://docs.specflow.org/projects/specflow-livingdoc/en/latest/LivingDocGenerator/Generating-Documentation.html)**

// Cucumber Expressions: https://docs.specflow.org/projects/specflow/en/latest/Bindings/Cucumber-Expressions.html
// TODO: Living Documentation
// TODO: with SpecFlow v4 - CucumberExpressions.SpecFlow.3-9 is no longer needed as nuget

/*
	PRINCIPLES
		No checks on 'total' pointerfileentries, chunks, ... --> test on additinoal ones
*/

@archive @file
Scenario Outline: Archive one file
	Given a BinaryFile "<RelativeName>" of size "<Size>"
	When archived to the <ToTier> tier
	Then 1 additional Chunk
	Then 1 additional Binary
	Then 1 additional PointerFileEntry
	Then 0 additional ChunkList
	Then BinaryFile "<RelativeName>" has a PointerFile and the PointerFileEntry is marked as exists
	Then the Chunk for BinaryFile "<RelativeName>" are in the <ActualTier> tier and are <HydratedStatus> and have OriginalLength <Size>

	Examples:
		| RelativeName | Size                     | ToTier  | ActualTier | HydratedStatus |
		| f1.txt       | BELOW_ARCHIVE_TIER_LIMIT | Cool	| Cool       | HYDRATED       |
		| f2.txt       | ABOVE_ARCHIVE_TIER_LIMIT | Cold    | Cold       | HYDRATED       |
		| f3.txt       | BELOW_ARCHIVE_TIER_LIMIT | Archive | Cold       | HYDRATED       |
		| f4 d.txt     | ABOVE_ARCHIVE_TIER_LIMIT | Archive | Archive    | NOT_HYDRATED   |

@archive @dedup
Scenario Outline: Archive one file deduplicated
	Given a BinaryFile "<RelativeName>" of size "<Size>" 
	When deduplicated and archived to the <ToTier> tier
	Then "<AdditionalChunks>" additional Chunks
	Then "<AdditionalBinaries>" additional Binaries
	Then <AdditionalChunkLists> additional ChunkLists
	Then 1 additional PointerFileEntry
	Then the Chunks for BinaryFile "<RelativeName>" are in the <ActualTier> tier and are <HydratedStatus> and have OriginalLength <Size>

	Examples:
		| RelativeName | Size                  | ToTier  | AdditionalChunks | AdditionalBinaries | AdditionalChunkLists | ActualTier | HydratedStatus |
		| df10.txt     | BELOW_CHUNKSIZE_LIMIT | Cool    | 1                | 1                  | 0                    | Cool       | HYDRATED       |
		| df11.txt     | APPROX_TEN_CHUNKS     | Cool    | MORE_THAN_ONE    | 1                  | 1                    | Cool       | HYDRATED       |
		| df12.txt     | APPROX_TEN_CHUNKS     | Archive | MORE_THAN_ONE    | 1                  | 1                    | Cold       | HYDRATED       |

@dedup
Scenario: ReArchive a deduplicated file
		# Archive a deduplicated file, and then archive as not deduplicated
	Given a BinaryFile "df20.txt" of size "APPROX_TEN_CHUNKS"
	When deduplicated and archived to the Cool tier
	Then "MORE_THAN_ONE" additional Chunks
	Then 1 additional Binary
	Then 1 additional PointerFileEntry

	Given a BinaryFile "df21.txt" duplicate of BinaryFile "df20.txt"
	When archived to the Cool tier

	Then 0 additional Chunks
	Then 0 additional Binaries
	Then 1 additional PointerFileEntry

		# The reverse: archive a file (not deduplicated) and then archive as deduplicated
	Given a BinaryFile "df22.txt" of size "APPROX_TEN_CHUNKS"
	When archived to the Cool tier
	Then 1 additional Chunks
	Then 1 additional Binaries
	Then 1 additional PointerFileEntry

	Given a BinaryFile "df23.txt" duplicate of BinaryFile "df22.txt"
	When deduplicated and archived to the Cool tier

	Then 0 additional Chunks
	Then 0 additional Binaries
	Then 1 additional PointerFileEntry


@archive @file @undelete
Scenario: Undelete a file
	# Archive initial file
	Given a BinaryFile "File2.txt" of size "BELOW_ARCHIVE_TIER_LIMIT"
	When archived to the Cool tier
	Then BinaryFile "File2.txt" has a PointerFile and the PointerFileEntry is marked as exists
	# Delete, then archive
	When BinaryFile "File2.txt" and its PointerFile are deleted
	When archived to the Cool tier
	Then 0 additional Chunks
	Then 0 additional Binaries
	Then the PointerFileEntry for BinaryFile "File2.txt" is marked as deleted
	# Restore
	When BinaryFile "File2.txt" is undeleted
	When archived to the Cool tier
	Then BinaryFile "File2.txt" has a PointerFile and the PointerFileEntry is marked as exists
	Then 0 additional Chunks
	Then 0 additional Binaries
	
@archive @file @duplicate
Scenario: Archive a duplicate file that was already archived
	Given a BinaryFile "File30.txt" of size "1 KB"
	When archived to the Cool tier
	Then 1 additional Chunk
	Then 1 additional Binary
	# Add the duplicate file
	Given a BinaryFile "File31.txt" duplicate of BinaryFile "File30.txt"
	When archived to the Cool tier
	Then 0 additional Chunks
	Then 0 additional Binaries
	Then BinaryFile "File30.txt" has a PointerFile and the PointerFileEntry is marked as exists
	Then BinaryFile "File31.txt" has a PointerFile and the PointerFileEntry is marked as exists
	
@archive @file @duplicate
Scenario: Archive duplicate files
	Given a BinaryFile "File40.txt" of size "1 KB"
	Given a BinaryFile "File41.txt" duplicate of BinaryFile "File40.txt"
	When archived to the Cool tier
	Then 1 additional Chunk
	Then 1 additional Binary
	Then BinaryFile "File40.txt" has a PointerFile and the PointerFileEntry is marked as exists
	Then BinaryFile "File41.txt" has a PointerFile and the PointerFileEntry is marked as exists

	Given a BinaryFile "File42.txt" duplicate of BinaryFile "File41.txt"
	When archived to the Cool tier
	Then 0 additional Chunks
	Then 0 additional Binaries
	Then BinaryFile "File42.txt" has a PointerFile and the PointerFileEntry is marked as exists
	# Then 1 additional pointerfileentry


Scenario: Archive a duplicate PointerFile
	Given a BinaryFile "File50.txt" of size "1 KB"
	When archived to the Cool tier
	Given a Pointer of BinaryFile "File51.txt" duplicate of the Pointer of BinaryFile "File50.txt"
	When archived to the Cool tier

	Then 0 additional Chunks
	Then 0 additional Binaries
	Then BinaryFile "File50.txt" has a PointerFile and the PointerFileEntry is marked as exists
	Then a PointerFileEntry for a BinaryFile "File51.txt" is marked as exists
	

Scenario: Rename BinaryFile with PointerFile
	Given a BinaryFile "File60.txt" of size "1 KB"
	When archived to the Cool tier
	When BinaryFile "File60.txt" and its PointerFile are moved to "subdir 1\File61.txt"
	When archived to the Cool tier

	Then 0 additional Chunks
	Then 0 additional Binaries
	Then the PointerFileEntry for BinaryFile "File60.txt" is marked as deleted
	Then a PointerFileEntry for a BinaryFile "subdir 1\File61.txt" is marked as exists


Scenario: Rename BinaryFile only
	Given a BinaryFile "File70.txt" of size "1 KB" 
	When archived to the Cool tier
	When BinaryFile "File70.txt" is moved to "subdir 2\File71.txt" 
	When archived to the Cool tier

	Then 0 additional Chunks
	Then 0 additional Binaries
	Then a PointerFileEntry for a BinaryFile "File70.txt" is marked as exists
	Then a PointerFileEntry for a BinaryFile "subdir 2\File71.txt" is marked as exists

	
Scenario: Archive with RemoveLocal
	Given a BinaryFile "File8.txt" of size "1 KB"
	When archived to the Cool tier with option RemoveLocal

	Then BinaryFile "File8.txt" no longer exists
	Then BinaryFile "File8.txt" has a PointerFile and the PointerFileEntry is marked as exists

	
Scenario: Rename PointerFile that no longer has a BinaryFile
	Given a BinaryFile "File90.txt" of size "1 KB" 
	When archived to the Cool tier with option RemoveLocal

	When the PointerFile for BinaryFile "File90.txt" is moved to "subdir 2\File91.txt"
	When archived to the Cool tier

	Then the PointerFileEntry for BinaryFile "File90.txt" is marked as deleted
		# NOTE these two steps do the same thing -- probably they can be refactored/merged to be more explicit on the intent of the BinaryFile existing 
	Then a PointerFileEntry for a BinaryFile "subdir 2\File91.txt" is marked as exists
	Then BinaryFile "subdir 2\File91.txt" has a PointerFile and the PointerFileEntry is marked as exists


@todo
Scenario: Corrupt Pointer
	#// garbage in the pointerfile (not a v1 pointer, not a sha hash)
	#var fn = Path.Combine(ArchiveTestDirectory.FullName, "fakepointer.pointer.arius");
	#await File.WriteAllTextAsync(fn, "kaka");
	#
	#var ae = Assert.CatchAsync<AggregateException>(async () => await ArchiveCommand());
	#var e = ae!.InnerExceptions.Single().InnerException;
	#Assert.IsInstanceOf<ArgumentException>(e);
	#Assert.IsTrue(e.Message.Contains("not a valid PointerFile"));

@todo
Scenario: Non Matching Pointer
#	// Stage a situation with a binary and a pointer
#    TestSetup.StageArchiveTestDirectory(out FileInfo bfi);
#    await ArchiveCommand();
#    var ps = GetPointerService();
#    var pf = ps.GetPointerFile(bfi);
#    // But the Pointer does not match
#    File.WriteAllLines(pf.FullName, new[] { "{\"BinaryHash\":\"aaaaaaaaaaaaa7da82bfb533db099d2e843ee5f03efa8657e9da1aca63396f4c\"}" });
#        
#    if (matchLastWriteTime)
#        File.SetLastWriteTimeUtc(pf.FullName, File.GetLastWriteTimeUtc(bfi.FullName));
#
#    var ae = Assert.CatchAsync<AggregateException>(async () => await ArchiveCommand());
#    var e = ae!.InnerExceptions.Single().InnerException;
#    Assert.IsInstanceOf<InvalidOperationException>(e);
#
#    if (matchLastWriteTime)
#        // LastWriteTime matches - Arius assumes the pointer belongs to the binaryfile but the hash doesnt match
#        Assert.IsTrue(e.Message.Contains("is not valid for the BinaryFile"));
#    else
#        // LastWriteTime does not match - Arius assumes this modified file, but can't find the binary anywhere
#        Assert.IsTrue(e.Message.Contains("exists on disk but no corresponding binary exists either locally or remotely"));

@todo
Scenario: Stale Pointer
#	//Create a 'stale' PointerFile that does not have a corresponding binary in the local or remote repository
#    var fn = Path.Combine(ArchiveTestDirectory.FullName, "fakepointer.pointer.arius");
#    await File.WriteAllTextAsync(fn, "{\"BinaryHash\":\"467bb39560918cea81c42dd922bb9aa71f20642fdff4f40ee83e3fade36f02be\"}");
#
#    var ae = Assert.CatchAsync<AggregateException>(async () => await ArchiveCommand());
#    var e = ae!.InnerExceptions.Single().InnerException;
#    Assert.IsInstanceOf<InvalidOperationException>(e);
#    Assert.IsTrue(e.Message.Contains("no corresponding binary exists either locally or remotely"));
#
#    File.Delete(fn);

@todo
Scenario: Update DateTime of a File or Pointer

@todo
Scenario: Delete a Pointer, archive, pointer is recreated

@todo
Scenario: Modify a binary with/without fasthash