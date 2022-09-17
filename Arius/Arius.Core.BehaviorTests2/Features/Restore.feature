Feature: Restore

A short summary of the feature

@tag1
Scenario: bla
	Given the following local files are archived:
  | ID    | RelativeFileName | Size                     |
  | File1 | dir1\\wouter.txt | 15 KB                    |
  | File2 | dir2\\joke.pdf   | BELOW_ARCHIVE_TIER_LIMIT |
  | File3 | taxes.doc        | BELOW_ARCHIVE_TIER_LIMIT |