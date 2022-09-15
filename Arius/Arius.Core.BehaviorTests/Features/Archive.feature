﻿Feature: Archive

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
Scenario Outline: Archive one file
	Given a remote archive
	Given a local folder with BinaryFile <FILE> of size <SIZE>
	When archived to the <TO_TIER> tier
	Then 1 additional Chunk and Manifest
	Then BinaryFile <FILE> has a PointerFile and the PointerFileEntry is marked as exists
	Then the Chunks for BinaryFile <FILE> are in the <ACTUAL_TIER> tier and are <HYDRATED_STATUS>

	Examples:
		| FILE    | SIZE                     | TO_TIER | ACTUAL_TIER | HYDRATED_STATUS |
		| File100 | BELOW_ARCHIVE_TIER_LIMIT | Cool    | Cool        | HYDRATED        |
		| File101 | ABOVE_ARCHIVE_TIER_LIMIT | Cool    | Cool        | HYDRATED        |
		| File102 | BELOW_ARCHIVE_TIER_LIMIT | Archive | Cool        | HYDRATED        |
		| File103 | ABOVE_ARCHIVE_TIER_LIMIT | Archive | Archive     | NOT_HYDRATED    |


#Scenario: Archive one file to the Archive tier
#	Given a remote archive
#	Given a local folder with BinaryFile File130 of size ARCHIVE_TIER_LIMIT


Scenario: Undelete a file
	# Archive initial file
	Given a remote archive
	Given a local folder with BinaryFile File2
	When archived to the Cool tier
	Then BinaryFile File2 has a PointerFile and the PointerFileEntry is marked as exists
	# Delete, then archive
	When BinaryFile File2 and its PointerFile are deleted
	When archived to the Cool tier
	Then 0 additional Chunks and Manifests
	Then BinaryFile File2 does not have a PointerFile and the PointerFileEntry is marked as deleted
	# Restore
	Given a local folder with BinaryFile File2
	When archived to the Cool tier
	Then BinaryFile File2 has a PointerFile and the PointerFileEntry is marked as exists
	Then 0 additional Chunks and Manifests
	

Scenario: Archive a duplicate file that was already archived
	Given a remote archive
	Given a local folder with BinaryFile File300
	When archived to the Cool tier
	# Add the duplicate file
	Given a local folder with BinaryFile File301 duplicate of BinaryFile File300
	When archived to the Cool tier
	Then BinaryFile File300 has a PointerFile and the PointerFileEntry is marked as exists
	Then BinaryFile File301 has a PointerFile and the PointerFileEntry is marked as exists
	Then 0 additional Chunks and Manifests
	

Scenario: Archive two duplicate files
	Given a remote archive
	
	Given a local folder with BinaryFile File400
	Given a local folder with BinaryFile File401 duplicate of BinaryFile File400
	When archived to the Cool tier
	Then 1 additional Chunk and Manifest
	Then BinaryFile File400 has a PointerFile and the PointerFileEntry is marked as exists
	Then BinaryFile File401 has a PointerFile and the PointerFileEntry is marked as exists

	Given a local folder with BinaryFile File402 duplicate of BinaryFile File401
	When archived to the Cool tier
	Then 0 additional Chunks and Manifests
	Then BinaryFile File402 has a PointerFile and the PointerFileEntry is marked as exists


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
#Given the following users exist:
#  | name   | email              | twitter         |
#  | Aslak  | aslak@cucumber.io  | @aslak_hellesoy |
#  | Julien | julien@cucumber.io | @jbpros         |
#  | Matt   | matt@cucumber.io   | @mattwynne      |
