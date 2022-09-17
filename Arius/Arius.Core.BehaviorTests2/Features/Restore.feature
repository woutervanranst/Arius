Feature: Restore

A short summary of the feature

@tag1
Scenario: bla
	Given the following local files are archived to Cool tier:
	  | RelativeName     | Size                     |
	  | dir1\\wouter.txt | 15 KB                    |
	  | dir2\\joke.pdf   | BELOW_ARCHIVE_TIER_LIMIT |
	  | taxes.doc        | BELOW_ARCHIVE_TIER_LIMIT |

Scenario: bla2
  Then haha