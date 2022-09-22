Feature: Restore Directory

A short summary of the feature

    Background:
        Given the following local files are archived to Cool tier:
		    | RelativeName     | Size                     | SourceRelativeName |
		    | dir1\\wouter.txt | 15 KB                    |                    |
		    | dir2\\joke.pdf   | BELOW_ARCHIVE_TIER_LIMIT |                    |
		    | taxes.doc        |                          | dir1\\wouter.txt   |

    @tag1
    Scenario: Synchronize and download a directory
        # Restore with Synchronize, Download, Directory
        Given a clean restore directory
	    When restore --synchronize --download --keepPointers
        Then all BinaryFiles and PointerFiles are restored successfully
	    

    Scenario: Synchronize a directory
        # Restore with Synchronize, NoDownload, Directory
        Given a clean restore directory
        When restore --synchronize --keepPointers
        Then all PointerFiles are restored succesfully


    Scenario: Download a directory
        # Restore with NoSynchronize, Download, Directory
