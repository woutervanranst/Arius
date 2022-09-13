Feature: Archive

Link to a feature: [Calculator](Arius.Core.BehaviorTests/Features/Calculator.feature)
***Further read***: **[Learn more about how to generate Living Documentation](https://docs.specflow.org/projects/specflow-livingdoc/en/latest/LivingDocGenerator/Generating-Documentation.html)**

// Cucumber Expressions: https://docs.specflow.org/projects/specflow/en/latest/Bindings/Cucumber-Expressions.html
// TODO: Living Documentation
//TODO: with SpecFlow v4 - CucumberExpressions.SpecFlow.3-9 is no longer needed as nuget

@mytag
Scenario: Archive one file
	Given a remote archive
	Given a local archive with file File1
	When archived to the Cool tier
	Then 1 additional Chunk
	Then 1 additional Manifest
	Then 1 additional total PointerFileEntry
	Then 1 additional existing PointerFileEntry
	#Then the file has a PointerFile
	Then all local files have PointerFiles and PointerFileEntries
	Then all chunks are in the Cool tier


Scenario: Undelete a file
	# Archive initial file
	Given a remote archive
	Given a local archive with file File1
	When archived to the Cool tier
	
	# Delete, then archive
	When the local archive is cleared
	When archived to the Cool tier
	Then 0 total existing PointerFileEntries
	Then 1 additional total PointerFileEntry
	Then 0 additional Chunks
	Then 0 additional Manifests
	Then File1 does not have a PointerFile
	Then the PointerFileEntry for File1 is marked as deleted

	# Restore
	Given a local archive with file File1
	When archived to the Cool tier
	Then 1 total existing PointerFileEntries
	Then 1 additional total PointerFileEntries
	Then 0 additional Chunks
	Then 0 additional Manifests
	

Scenario: Archive a duplicate file that was already archived
	Given a remote archive
	Given a local archive with file File100
	When archived to the Cool tier

	# Add the duplicate file
	Given a local archive with file File101 duplicate of File100
	When archived to the Cool tier
	Then 0 additional Chunks
	Then 0 additional Manifests
	Then 1 additional existing PointerFileEntry
	Then all local files have PointerFiles and PointerFileEntries
	
Scenario: Archive two duplicate files
	Given a remote archive
	Given a local archive with file File110
	
	Given a local archive with file File111 duplicate of File110
	When archived to the Cool tier
	Then 1 additional Chunk
	Then 1 additional Manifest
	Then 2 additional existing PointerFileEntries
	Then all local files have PointerFiles and PointerFileEntries

	Given a local archive with file File112 duplicate of File110
	When archived to the Cool tier
	Then 0 additional Chunks
	Then 0 additional Manifests
	Then 1 additional existing PointerFileEntry
	Then all local files have PointerFiles and PointerFileEntries


Scenario: Archive a duplicate PointerFile
	Given a remote archive
	Given a local archive with file File120
	When archived to the Cool tier

	Given a duplicate Pointer of file File120
	When archived to the Cool tier

	Then 0 additional Chunks
	Then 0 additional Manifests
	Then 1 additional existing PointerFileEntry
	Then 2 PointerFiles exist
	Then all local PointerFiles have PointerFileEntries
	

#Scenario: Archive3
#	Given a local repository with files
#	* file 1 with 2 KB
#	* file 2 with 1 KB
