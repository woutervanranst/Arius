Feature: Restore File

Background:
    Given a clean archive directory
    When the following BinaryFiles are archived to Cool tier:
		| RelativeName     | Size                     | SourceRelativeName |
		| dir1\\wouter.txt | 15 KB                    |                    |
		| dir2\\joke.pdf   | BELOW_ARCHIVE_TIER_LIMIT |                    |
		| taxes.doc        |                          | dir1\\wouter.txt   |
            # taxes.doc and wouter.txt will have the same chunks

@restore @dedup
Scenario: Restore a deduplicated file
    Given a BinaryFile "File200.txt" of size "APPROX_TEN_CHUNKS"
    When deduplicated and archived to the Cool tier

    Given a clean restore directory
    When restore --synchronize --download
    Then all BinaryFiles are restored successfully

# Test the --synchronize flag of the restore command
@restore
Scenario: Synchronization removes obsolete pointers but leaves binaryfiles intact
    Given a BinaryFile "File2.txt" of size "BELOW_ARCHIVE_TIER_LIMIT"
    When archived to the Cool tier
    Given a clean restore directory
    Given a random PointerFile for BinaryFile "test.txt"
    Given a random BinaryFile "profile.jpg"
    When restore --synchronize

    # The binaryfile is not removed
    Then only the BinaryFile "profile.jpg" is present
    # all pointerfiles that shoud exist, exist
    Then all PointerFiles are restored successfully
    # all other pointerfiles are deleted
    Then the PointerFile for BinaryFile "test.txt" does not exist


Scenario: Synchronize and download a file
    Given a clean restore directory
        # NOTE this gets converted to a platform specific in the codebehind
    When restore relativename "dir1\\wouter.txt"   
    Then only the BinaryFile "dir1\\wouter.txt" is present

    # Restore with Synchronize, Download, File is NOT SUPPORTED

@todo
Scenario: Synchronize a file
    # Restore with Synchronize, NoDownload, File is NOT SUPPORTED

@todo
Scenario: Download a file
    # Restore with NoSynchronize, Download, File is NOT SUPPORTED