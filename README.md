# Arius

![Build](https://github.com/woutervanranst/Arius/workflows/Build/badge.svg)

Arius is a lightweight tiered archival solution, specifically built to leverage the Azure Blob Archive tier.

The name derives from the Greek for 'immortal'.

- [Arius](#arius)
  - [Key design objectives](#key-design-objectives)
  - [Usage](#usage)
    - [Archive to blob storage](#archive-to-blob-storage)
    - [Restore from blob storage](#restore-from-blob-storage)
    - [Restore with common tools](#restore-with-common-tools)
  - [Install](#install)
    - [Linux](#linux)
    - [Windows](#windows)
    - [Docker](#docker)

## Key design objectives

* Local file structure (files/folders) by creating 'sparse' placeholders
* Files, folders & filenames are encrypted clientside
* The local filestructure is _not_ reflected in the archive structure (ie it is obfuscated)
* Changes in the local file _structure_ do not cause a reshuffle in the archive (which doesn't sit well with Archive storage)
* Never delete files on remote
* Point in time restore (FUTURE)
* No central store to avoid a single point of failure
* File level deduplication
* Leverage common tools, to allow restores even when this project would become deprecated

## Usage

### Archive to blob storage

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
Synchronize the `<path>` to the the remote archive.

``--container`` the container name to use. Default: ``arius``

``--keep-local`` do not delete the local files after archiving. Default: delete after archiving

``--tier`` specify the blob tier. Default: archive

``--min-size`` the minimum size as of which to archive files. Default: 1 MB. WARNING if >0 then a full restore will miss the smaller files

### Restore from blob storage

```
arius restore
   --accountname <accountname>
   --accountkey <accountkey>
   --passphrase <passphrase>
  (--container <containername>)
  (--synchronize)
  (--download)
  <path>
```

If `<path>` is a Directory:

Synchronize the remote archive structure to the `<path>`:

* This command only touches the pointers (ie. `.arius` files). Other files are left untouched.
* Pointers that exist in the archive but not remote are created
* Pointers that exist locally but not in the archive are deleted

When the `--download` option is specified, the files are also downloaded WARNING this may consume a lot of bandwidth and may take a long time

If ``<path>`` is an `.arius` file `--download` flag is specified: the file is restored.

### Restore with common tools

Arius relies on the 7zip command line and Azure blob storage cli.

## Install

### Linux

Run the following commands:
<!-- from https://blog.markvincze.com/download-artifacts-from-a-latest-github-release-in-sh-and-powershell/ -->
```
LATEST_RELEASE=$(curl -L -s -H 'Accept: application/json' https://github.com/woutervanranst/arius/releases/latest)
LATEST_VERSION=$(echo $LATEST_RELEASE | sed -e 's/.*"tag_name":"\([^"]*\)".*/\1/')
ARTIFACT_URL="https://github.com/woutervanranst/arius/releases/download/$LATEST_VERSION/release.zip"
wget $ARTIFACT_URL
unzip release.zip
dotnet arius.dll ...
```

### Windows

TODO

### Docker