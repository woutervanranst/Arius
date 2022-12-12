Feature: Restore Synchronize

Test the --synchronize flag of the restore command

Scenario: Synchronization removes obsolete pointers but leaves binaryfiles intact
    Given a BinaryFile "File2.txt" of size "BELOW_ARCHIVE_TIER_LIMIT" is archived to the Cool tier
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

