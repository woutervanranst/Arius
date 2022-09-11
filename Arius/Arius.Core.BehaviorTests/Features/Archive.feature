Feature: Archive

Link to a feature: [Calculator](Arius.Core.BehaviorTests/Features/Calculator.feature)
***Further read***: **[Learn more about how to generate Living Documentation](https://docs.specflow.org/projects/specflow-livingdoc/en/latest/LivingDocGenerator/Generating-Documentation.html)**

@mytag
Scenario: Archive
	Given an exstisting remote archive
	Given an existing local archive with one file
	When I archive
	Then the files should be archived


Scenario: Archive2
	Given an empty remote archive
	Given a local archive with 1 file
	When archived
	Then 1 additional chunks uploaded

#Scenario: Archive3
#	Given a local repository with files
#	* file 1 with 2 KB
#	* file 2 with 1 KB
