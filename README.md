# Arius
Arius is a lightweight tiered archival solution, specifically built to leverage the Azure Blob Archive tier. 

The name derives from the Greek for 'immortal'. 

## Key design objectives
* Maintain local file structure (files/folders) by creating 'sparse' placeholders
* Files, folders & filenames are encrypted clientside
* The local filestructure is _not_ reflected in the archive structure (ie it is obfuscated)
* Changes in the local file _structure_ do not cause a reshuffle in the archive (which doesn't sit well with Archive storage)
* Never delete files on remote
* Point in time restore (FUTURE)
* No central store to avoid a single point of failure
* Leverage common tools, to allow restores even when this project would become deprecated

## CLI

### Archive to Blob
Archive the current path Azure

```
arius archive 
   --accountname <accountname> 
   --accountkey <accountkey> 
   --passphrase <passphrase>
  (--container <containername>) 
  (--keep-local)
  (--tier=(hot/cool/archive))
  (--min-size=<minsizeinMB>)
  (--simulate)
  <path>
```

``--container`` the container name to use. Default: ``arius``

``--keep-local`` do not delete the local files after archiving. Default: delete after archiving

``--tier`` specify the blob tier. Default: archive

``--min-size`` the minimum size as of which to archive files. Default: 1 MB. WARNING if >0 then a full restore will miss the smaller files


### Restore from blob
Restore the archive structure to the current path.

```
arius restore
   --accountname <accountname> 
   --accountkey <accountkey> 
   --passphrase <passphrase>
  (--container <containername>) 
  (--download)
  <path>
```

``--download`` also download the blobs WARNING this may consume a lot of bandwidth and may take a long time

``path``
* Empty Directory > Full Restore
* Directory with .arius files > Restore all files in the directory
* Arius file > restore this file

## Restore with common tools

Arius relies on the 7zip command line and Azure blob storage cli.
