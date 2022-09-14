Feature: Archive

Link to a feature: [Calculator](Arius.Core.BehaviorTests/Features/Calculator.feature)
***Further read***: **[Learn more about how to generate Living Documentation](https://docs.specflow.org/projects/specflow-livingdoc/en/latest/LivingDocGenerator/Generating-Documentation.html)**

// Cucumber Expressions: https://docs.specflow.org/projects/specflow/en/latest/Bindings/Cucumber-Expressions.html
// TODO: Living Documentation
//TODO: with SpecFlow v4 - CucumberExpressions.SpecFlow.3-9 is no longer needed as nuget

@mytag
Scenario: Archive one file
	Given a remote archive
	Given a local folder with only file File1
	When archived to the Cool tier
	Then 1 additional Chunk
	Then 1 additional Manifest
	#Then 1 additional total PointerFileEntry
	Then 1 total existing PointerFileEntry
	Then the PointerFile for file File1 exists
	Then all chunks are in the Cool tier

#Scenario: Archive one file to the Archive tier
#	Given a remote archive
#	Given a local folder with file File130 of size ARCHIVE_TIER_LIMIT

Scenario: Undelete a file
	# Archive initial file
	Given a remote archive
	Given a local folder with file File1
	When archived to the Cool tier
	Then 1 additional existing PointerFileEntries
	
	# Delete, then archive
	When the local folder is cleared
	When archived to the Cool tier
	Then 0 total existing PointerFileEntries
	Then 0 additional Chunks
	Then 0 additional Manifests
	Then File1 does not have a PointerFile
	Then the PointerFileEntry for File1 is marked as deleted

	# Restore
	Given a local folder with file File1
	When archived to the Cool tier
	Then 1 total existing PointerFileEntries
	Then 1 additional total PointerFileEntry
	Then 0 additional Chunks
	Then 0 additional Manifests
	

Scenario: Archive a duplicate file that was already archived
	Given a remote archive
	Given a local folder with file File100
	When archived to the Cool tier

	# Add the duplicate file
	Given a local folder with file File101 duplicate of file File100
	When archived to the Cool tier
	Then 0 additional Chunks
	Then 0 additional Manifests
	Then 1 additional existing PointerFileEntry
	Then all local files have PointerFiles and PointerFileEntries
	

Scenario: Archive two duplicate files
	Given a remote archive
	Given a local folder with file File110
	
	Given a local folder with file File111 duplicate of file File110
	When archived to the Cool tier
	Then 1 additional Chunk
	Then 1 additional Manifest
	Then 2 additional existing PointerFileEntries
	Then all local files have PointerFiles and PointerFileEntries

	Given a local folder with file File112 duplicate of file File111
	When archived to the Cool tier
	Then 0 additional Chunks
	Then 0 additional Manifests
	Then 1 additional existing PointerFileEntry
	Then all local files have PointerFiles and PointerFileEntries


Scenario: Archive a duplicate PointerFile
	Given a remote archive
	Given a local folder with file File120
	When archived to the Cool tier

	Given a duplicate Pointer Pointer121 of file File120
	When archived to the Cool tier

	Then 0 additional Chunks
	Then 0 additional Manifests
	Then 1 additional existing PointerFileEntry
	#Then 2 additional PointerFiles exist
	Then the PointerFile for file File120 exists
	Then the PointerFile Pointer121 exists
	#Then all local PointerFiles have PointerFileEntries
	

#Scenario: Archive3
#	Given a local repository with files
#	* file 1 with 2 KB
#	* file 2 with 1 KB
