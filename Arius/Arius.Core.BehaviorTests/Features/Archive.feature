Feature: Archive

Link to a feature: [Calculator](Arius.Core.BehaviorTests/Features/Calculator.feature)
***Further read***: **[Learn more about how to generate Living Documentation](https://docs.specflow.org/projects/specflow-livingdoc/en/latest/LivingDocGenerator/Generating-Documentation.html)**

@mytag
Scenario: Archive one file
	Given a remote archive
	Given a local archive with 1 file
	When archived to the Cool tier
	Then 1 additional Chunk
	Then 1 additional Manifest
	Then 1 additional total PointerFileEntry
	Then 1 additional existing PointerFileEntry
	#Then the file has a PointerFile
	Then all local files have PointerFiles and PointerFileEntries
	Then all chunks are in the Cool tier

Scenario: Undelete a file
	Given a remote archive
	Given a local archive with 1 file
	# 1st Archive
	When archived to the Cool tier
	When the local archive is cleared
	# 2nd Archive
	When archived to the Cool tier
	Then 0 total existing PointerFileEntries
	Then 1 additional total PointerFileEntry
	Then 0 additional Chunks
	Then 0 additional Manifests
	Given a local archive with 1 file
	# 3rd Archive
	When archived to the Cool tier
	Then 1 total existing PointerFileEntries


#Scenario: Archive3
#	Given a local repository with files
#	* file 1 with 2 KB
#	* file 2 with 1 KB
