Feature: Restore File

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


@todo
Scenario: Synchronize and download a file
    # Restore with Synchronize, Download, File is NOT SUPPORTED

@todo
Scenario: Synchronize a file
    # Restore with Synchronize, NoDownload, File is NOT SUPPORTED

@todo
Scenario: Download a file
    # Restore with NoSynchronize, Download, File is NOT SUPPORTED