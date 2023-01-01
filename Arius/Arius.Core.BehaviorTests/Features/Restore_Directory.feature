Feature: Restore Directory

A short summary of the feature

TODO / BACKLOG
* empty dir
* remote with isdeleted and local present > should be deleted
* remote with !isdeleted and local not present > should be created
* also in subdirectories
* in ariusfile : de verschillende extensions
* al 1 file lokaal > kopieert de rest
* directory more than 2 deep without other files
*  download > BinaryFiles exist s> don't download all
* restore naar directory waar al andere bestanden (binaries) instaan -< are not touched (dan moet ge maa rnaar ne lege restoren)
* restore a seoncd time without any changes
* dedup / chunks weg


Background:
    Given a clean archive directory
    Given the following BinaryFiles are archived to Cool tier:
		| RelativeName     | Size                     | SourceRelativeName |
		| dir1\\wouter.txt | 15 KB                    |                    |
		| dir2\\joke.pdf   | BELOW_ARCHIVE_TIER_LIMIT |                    |
		| taxes.doc        |                          | dir1\\wouter.txt   |
            # taxes.doc and wouter.txt will have the same chunks

@tag1
Scenario: Synchronize and download a directory
    # Restore with Synchronize, Download, Directory
    Given a clean restore directory
	When restore --synchronize --download --keepPointers
    Then all BinaryFiles are restored successfully
    Then all PointerFiles are restored successfully
	    

Scenario: Synchronize a directory and keep pointers
    # Restore with Synchronize, NoDownload, Directory
    Given a clean restore directory
    When restore --synchronize --keepPointers
    Then all PointerFiles are restored successfully
    Then no BinaryFiles are present


Scenario: Synchronize a directory and do not keep pointers
    # Restore with Synchronize, NoDownload, Directory
    Given a clean restore directory
    When restore --synchronize
    Then all PointerFiles are restored successfully
    Then no BinaryFiles are present


Scenario: Synchronize and download a directory and do not keep pointers
    Given a clean restore directory
    When restore --synchronize --download
    Then all BinaryFiles are restored successfully
    Then no PointerFiles are present


Scenario: Selective restore: Download a directory and keep pointers
    # Restore with NoSynchronize, Download, KeepPointers, Directory
    Given a clean restore directory
    When restore --download --keepPointers
    Then no PointerFiles are present
    Then no BinaryFiles are present

    When copy the PointerFile of BinaryFile "dir1\wouter.txt" to the restore directory
    When restore --download --keepPointers
    Then only the PointerFile for BinaryFile "dir1\wouter.txt" is present
    Then only the BinaryFile "dir1\wouter.txt" is present


Scenario: Selective restore: Download a directory and do not keep pointers
    # Restore with NoSynchronize, Download, NoKeepPointers, Directory
    Given a clean restore directory
    When restore --download
    Then no PointerFiles are present
    Then no BinaryFiles are present

    When copy the PointerFile of BinaryFile "dir1\wouter.txt" to the restore directory
    When restore --download
    Then only the BinaryFile "dir1\wouter.txt" is present
    Then no PointerFiles are present
    

Scenario: Restore without synchronize and without download
    Given a clean restore directory
    When restore expect a ValidationException


Scenario: Download a file of which the binary is already restored
    Given a clean restore directory
    When copy the PointerFile of BinaryFile "dir1\wouter.txt" to the restore directory
    When copy the PointerFile of BinaryFile "taxes.doc" to the restore directory
    When restore --download
    Then the BinaryFile "dir1\wouter.txt" is restored from online tier
    Then the BinaryFile "taxes.doc" is restored from local