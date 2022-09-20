Feature: Restore

A short summary of the feature

@tag1
Scenario: Restore one file
	Given a local file "r1.txt" of size "ABOVE_ARCHIVE_TIER_LIMIT" is archived to the Cool tier 
	When restored
	Then all files are restoreed successfully


#Scenario: bla
#	Given the following local files are archived to Cool tier:
#	  | RelativeName     | Size                     |
#	  | dir1\\wouter.txt | 15 KB                    |
#	  | dir2\\joke.pdf   | BELOW_ARCHIVE_TIER_LIMIT |
#	  | taxes.doc        | BELOW_ARCHIVE_TIER_LIMIT |

