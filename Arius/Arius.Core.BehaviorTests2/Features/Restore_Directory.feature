Feature: Restore Directory

A short summary of the feature

    Background:
        Given the following local files are archived to Cool tier:
		    | RelativeName     | Size                     |
		    | dir1\\wouter.txt | 15 KB                    |
		    | dir2\\joke.pdf   | BELOW_ARCHIVE_TIER_LIMIT |
		    | taxes.doc        | BELOW_ARCHIVE_TIER_LIMIT |

    @tag1
    Scenario: Synchronize and download a directory
        # Restore with Synchronize, Download, Directory

	    When restored
	    Then all files are restored successfully

    Scenario: Synchronize a directory
        # Restore with Synchronize, NoDownload, Directory

    Scenario: Download a directory
        # Restore with NoSynchronize, Download, Directory
