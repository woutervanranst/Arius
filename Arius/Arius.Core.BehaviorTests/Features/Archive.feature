Feature: Archive

Link to a feature: [Calculator](Arius.Core.BehaviorTests/Features/Calculator.feature)
***Further read***: **[Learn more about how to generate Living Documentation](https://docs.specflow.org/projects/specflow-livingdoc/en/latest/LivingDocGenerator/Generating-Documentation.html)**

@mytag
Scenario: Archive
	Given an exstisting remote archive
	Given an existing local archive with one file
	When I archive
	Then the files should be archived


Scenario: Archive one file
	Given an empty remote archive
	Given a local archive with 1 file
	When archived
	Then 1 additional chunk
	Then 1 additional manifest
	Then 1 additional PointerFileEntry
	Then 1 additional existing PointerFileEntry
	#Then the file has a PointerFile
	Then all local files have PointerFiles

#Scenario: Archive3
#	Given a local repository with files
#	* file 1 with 2 KB
#	* file 2 with 1 KB
