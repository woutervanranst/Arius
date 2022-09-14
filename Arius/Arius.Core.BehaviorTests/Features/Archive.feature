Feature: Archive

Link to a feature: [Calculator](Arius.Core.BehaviorTests/Features/Calculator.feature)
***Further read***: **[Learn more about how to generate Living Documentation](https://docs.specflow.org/projects/specflow-livingdoc/en/latest/LivingDocGenerator/Generating-Documentation.html)**

// Cucumber Expressions: https://docs.specflow.org/projects/specflow/en/latest/Bindings/Cucumber-Expressions.html
// TODO: Living Documentation
//TODO: with SpecFlow v4 - CucumberExpressions.SpecFlow.3-9 is no longer needed as nuget

/*
	PRINCIPLES

		No checks on 'total' pointerfileentries, chunks, ... --> test on additinoal ones
*/

@mytag
Scenario: Archive one file
	Given a remote archive
	Given a local folder with BinaryFile File1
	When archived to the Cool tier
	Then 1 additional Chunk and Manifest
	Then 1 additional existing PointerFileEntry
	Then the PointerFile for file File1 exists
	Then all chunks are in the Cool tier


#Scenario: Archive one file to the Archive tier
#	Given a remote archive
#	Given a local folder with BinaryFile File130 of size ARCHIVE_TIER_LIMIT


Scenario: Undelete a file
	# Archive initial file
	Given a remote archive
	Given a local folder with BinaryFile File2
	When archived to the Cool tier
	Then 1 additional existing PointerFileEntry
	# Delete, then archive
	When BinaryFile File2 and its PointerFile are deleted
	When archived to the Cool tier
	Then 0 additional Chunks and Manifests
	Then BinaryFile File2 does not have a PointerFile and the PointerFileEntry is marked as deleted
	# Restore
	Given a local folder with BinaryFile File2
	When archived to the Cool tier
	Then BinaryFile File2 has a PointerFile and the PointerFileEntry is marked as exists
	Then 1 additional existing PointerFileEntries
	Then 0 additional Chunks and Manifests
	

Scenario: Archive a duplicate file that was already archived
	Given a remote archive
	Given a local folder with BinaryFile File100
	When archived to the Cool tier
	# Add the duplicate file
	Given a local folder with BinaryFile File101 duplicate of BinaryFile File100
	When archived to the Cool tier
	Then 0 additional Chunks and Manifests
	Then 1 additional existing PointerFileEntry
	Then all local files have PointerFiles and PointerFileEntries
	

Scenario: Archive two duplicate files
	Given a remote archive
	Given a local folder with BinaryFile File110
	
	Given a local folder with BinaryFile File111 duplicate of BinaryFile File110
	When archived to the Cool tier
	Then 1 additional Chunk and Manifest
	Then 2 additional existing PointerFileEntries
	Then all local files have PointerFiles and PointerFileEntries

	Given a local folder with BinaryFile File112 duplicate of BinaryFile File111
	When archived to the Cool tier
	Then 0 additional Chunks and Manifests
	Then 1 additional existing PointerFileEntry
	Then all local files have PointerFiles and PointerFileEntries


Scenario: Archive a duplicate PointerFile
	Given a remote archive
	Given a local folder with BinaryFile File120
	When archived to the Cool tier

	Given a duplicate Pointer Pointer121 of file File120
	When archived to the Cool tier
	
	Then 0 additional Chunks and Manifests
	Then 1 additional existing PointerFileEntry
	#Then 2 additional PointerFiles exist
	Then the PointerFile for file File120 exists
	Then the PointerFile Pointer121 exists
	#Then all local PointerFiles have PointerFileEntries
	

#Scenario: Archive3
#	Given a local repository with files
#	* file 1 with 2 KB
#	* file 2 with 1 KB
