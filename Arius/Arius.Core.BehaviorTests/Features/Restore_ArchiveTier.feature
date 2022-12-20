Feature: Restore from Archive Tier

A short summary of the feature

Scenario: B_Restore a file from archive tier
	Given a BinaryFile "File3.txt" of size "ABOVE_ARCHIVE_TIER_LIMIT" is archived to the Archive tier
	When copy the PointerFile of BinaryFile "File3.txt" to the restore directory
    When restore --download

	Then the hydration for the chunks of BinaryFile "File3.txt" have started
		# It cannot be restored yet
	Then the BinaryFile "File3.txt" does not exist


	# NOTE the A_ and B_ name are to (workaround) determine the order of execution.
	# Currently, the behavior of Arius is undefined when running a restore operation while a previous restore operation still has a rehydrate ongoing
Scenario: A_Restore a file from archive tier after the chunk has been hydrated
	# Stage
	Given a BinaryFile "File4.txt" of size "ABOVE_ARCHIVE_TIER_LIMIT" is archived to the Cool tier
	When copy the PointerFile of BinaryFile "File4.txt" to the restore directory
	When the chunk of BinaryFile "File4.txt" is copied to the rehydrate folder and the original chunk is moved to the Archive tier
	When restore --download

	Then the BinaryFile "File4.txt" exists
	Then the rehydrate folder does not exist



	