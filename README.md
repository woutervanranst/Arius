# Arius
Auius is a lightweight tiered archival solution, specifically built to leverage the Azure Blob Archive tier

The name derives from the Greek for 'immortal'

## Key design objectives
* Maintain local file structure (files/folders) by creating 'sparse' placeholders
* Files, folders & filenames are encrypted clientside
* The local filestructure is _not_ reflected in the archive structure (ie it is obfuscated)
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
```

``--container`` the container name to use. Default: ``arius``

``--keep-local`` do not delete the local files after archiving. Default: delete after archiving

``--tier`` specify the blob tier. Default: archive


### Restore from blob
Restore the archive structure to the current path.

```
arius restore
   --accountname <accountname> 
   --accountkey <accountkey> 
   --passphrase <passphrase>
  (--download)
```

``--download`` also download the blobs WARNING this may consume a lot of bandwidth and may take a long time

## Restore with common tools

Arius relies on the 7zip command line and Azure blob storage cli.
