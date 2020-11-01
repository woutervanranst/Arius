# Arius
Auius is a lightweight tiered archival solution for azure blob archive tier

The name derives from the Greek for 'immortal'

## Key design objectives
* Never delete files on remote
* Point in time restore
* Files and filenames are encrypted
* Local file and folder structure is maintained with 'larger' files moved to blob storage 
* Restore using common tools / Ie when aurius would no longer exist 
* No central store / no single point of failure / redolent ad robusg
* Client side encryption and a flat (obfuscated) structure on the target

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

