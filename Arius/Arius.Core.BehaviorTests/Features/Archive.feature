Feature: Archive

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
	Given a BinaryFile "<RelativeName>" of size "<Size>" is archived to the <ToTier> tier
	Then 1 additional Chunk and Manifest
	Then BinaryFile "<RelativeName>" has a PointerFile and the PointerFileEntry is marked as exists
	Then the Chunks for BinaryFile "<RelativeName>" are in the <ActualTier> tier and are <HydratedStatus>

	Examples:
		| RelativeName | Size                     | ToTier  | ActualTier | HydratedStatus |
		| f1.txt       | BELOW_ARCHIVE_TIER_LIMIT | Cool    | Cool       | HYDRATED       |
		| f2.txt       | ABOVE_ARCHIVE_TIER_LIMIT | Cool    | Cool       | HYDRATED       |
		| f3.txt       | BELOW_ARCHIVE_TIER_LIMIT | Archive | Cool       | HYDRATED       |
		| f4 d.txt     | ABOVE_ARCHIVE_TIER_LIMIT | Archive | Archive    | NOT_HYDRATED   |

@archive @file @undelete
Scenario: Undelete a file
	# Archive initial file
	Given a BinaryFile "File2.txt" of size "BELOW_ARCHIVE_TIER_LIMIT" is archived to the Cool tier
	Then BinaryFile "File2.txt" has a PointerFile and the PointerFileEntry is marked as exists
	# Delete, then archive
	When BinaryFile "File2.txt" and its PointerFile are deleted
	When archived to the Cool tier
	Then 0 additional Chunks and Manifests
	Then the PointerFileEntry for BinaryFile "File2.txt" is marked as deleted
	# Restore
	When BinaryFile "File2.txt" is undeleted
	When archived to the Cool tier
	Then BinaryFile "File2.txt" has a PointerFile and the PointerFileEntry is marked as exists
	Then 0 additional Chunks and Manifests
	
@archive @file @duplicate
Scenario: Archive a duplicate file that was already archived
	Given a BinaryFile "File30.txt" of size "1 KB" is archived to the Cool tier
	Then 1 additional Chunks and Manifests
	# Add the duplicate file
	Given a BinaryFile "File31.txt" duplicate of BinaryFile "File30.txt"
	When archived to the Cool tier
	Then 0 additional Chunks and Manifests
	Then BinaryFile "File30.txt" has a PointerFile and the PointerFileEntry is marked as exists
	Then BinaryFile "File31.txt" has a PointerFile and the PointerFileEntry is marked as exists
	
@archive @file @duplicate
Scenario: Archive duplicate files
	Given a BinaryFile "File40.txt" of size "1 KB"
	Given a BinaryFile "File41.txt" duplicate of BinaryFile "File40.txt"
	When archived to the Cool tier
	Then 1 additional Chunk and Manifest
	Then BinaryFile "File40.txt" has a PointerFile and the PointerFileEntry is marked as exists
	Then BinaryFile "File41.txt" has a PointerFile and the PointerFileEntry is marked as exists

	Given a BinaryFile "File42.txt" duplicate of BinaryFile "File41.txt"
	When archived to the Cool tier
	Then 0 additional Chunks and Manifests
	Then BinaryFile "File42.txt" has a PointerFile and the PointerFileEntry is marked as exists


Scenario: Archive a duplicate PointerFile
	Given a BinaryFile "File50.txt" of size "1 KB" is archived to the Cool tier
	Given a Pointer of BinaryFile "File51.txt" duplicate of the Pointer of BinaryFile "File50.txt"
	When archived to the Cool tier

	Then 0 additional Chunks and Manifests
	Then BinaryFile "File50.txt" has a PointerFile and the PointerFileEntry is marked as exists
	Then a PointerFileEntry for a BinaryFile "File51.txt" is marked as exists
	

Scenario: Rename BinaryFile with PointerFile
	Given a BinaryFile "File60.txt" of size "1 KB" is archived to the Cool tier
	When BinaryFile "File60.txt" and its PointerFile are moved to "subdir 1\File61.txt"
	When archived to the Cool tier

	Then 0 additional Chunk and Manifest
	Then the PointerFileEntry for BinaryFile "File60.txt" is marked as deleted
	Then a PointerFileEntry for a BinaryFile "subdir 1\File61.txt" is marked as exists


Scenario: Rename BinaryFile only
	Given a BinaryFile "File70.txt" of size "1 KB" is archived to the Cool tier
	When BinaryFile "File70.txt" is moved to "subdir 2\File71.txt" 
	When archived to the Cool tier

	Then 0 additional Chunks and Manifests
	Then a PointerFileEntry for a BinaryFile "File70.txt" is marked as exists
	Then a PointerFileEntry for a BinaryFile "subdir 2\File71.txt" is marked as exists

	
Scenario: Archive with RemoveLocal
	Given a BinaryFile "File8.txt" of size "1 KB" is archived to the Cool tier with option RemoveLocal

	Then BinaryFile "File8.txt" no longer exists
	Then BinaryFile "File8.txt" has a PointerFile and the PointerFileEntry is marked as exists
	#Then a PointerFileEntry for a BinaryFile "File8.txt" is marked as exists
	#Then BinaryFile File8 has a PointerFile and the PointerFileEntry is marked as exists
#
#Scenario: Rename PointerFile that no longer has a BinaryFile
#	Given a remote archive
#	Given a local folder with BinaryFile File9
#	When archived to the Cool tier with option RemoveLocal
#
#	When the PointerFile for BinaryFile File9 is renamed and moved to a subdirectory
#	When archived to the Cool tier
#
#	Then the PointerFile at the old location no longer exist and the PointerFileEntry is marked as deleted
#	#Then the PointerFile for BinaryFile File9 exists and the PointerFileEntry is marked as exists
#	#Then THE POINTERFILE AT THE NEW LOCATION EXISTS