# Arius

<img src="docs/iceberg.svg" width="200" />

![Arius.Tests](https://github.com/woutervanranst/Arius/workflows/Arius.Tests/badge.svg)

Arius is a lightweight tiered archival solution, specifically built to leverage the Azure Blob Archive tier.

The name derives from the Greek for 'immortal'.

- [Arius](#arius)
  - [Key design scenarios](#key-design-scenarios)
  - [Key design objectives](#key-design-objectives)
  - [Overview](#overview)
    - [Archive](#archive)
    - [Restore](#restore)
  - [Usage](#usage)
    - [Archive to blob storage](#archive-to-blob-storage)
      - [CLI](#cli)
      - [Docker Run](#docker-run)
      - [Arguments](#arguments)
    - [Restore from blob storage](#restore-from-blob-storage)
  - [Install](#install)
    - [Linux](#linux)
    - [Docker](#docker)
  - [Advanced](#advanced)
    - [Functional Flows](#functional-flows)
      - [Archive](#archive-1)
      - [Restore](#restore-1)
    - [Restore with common tools](#restore-with-common-tools)
    - [Deduplication](#deduplication)
  - [Developer reference](#developer-reference)
    - [Flow Walkthrough](#flow-walkthrough)
      - [Archive](#archive-2)
      - [Debugging Docker in Visual Studio](#debugging-docker-in-visual-studio)
- [Attributions](#attributions)

## Key design scenarios

- I have a lot of static files that I rarely access but don't want to lose (think: backups, family pictures & videos).
- For some of these, I keep a live copy in my Synology
- For all of these, I keep an offline copy on a disconnected harddisk
- To account for the mechanical failure of the harddisk (and to implement the [3-2-1 backup strategy](https://en.wikipedia.org/wiki/Backup#Storage)) I back up the entire hard disk to Azure using Arius. The price for this is approx. 1 EUR per TB per month.

## Key design objectives

- [x] Local file structure (files/folders) by creating 'sparse' placeholders
- [x] Files, folders & filenames are encrypted clientside
- [x] The local filestructure is _not_ reflected in the archive structure (ie it is obfuscated)
- [x] Changes in the local file _structure_ do not cause a reshuffle in the archive (which doesn't sit well with Archive storage)
- [x] Never delete files on remote
- [x] No central store to avoid a single point of failure
- [x] File level deduplication
- [x] Variable block size (rolling hash Rabin-Karp) deduplication
- [x] Leverage common tools, to allow restores even when this project would become deprecated
- [ ] Point in time restore (FUTURE)

## Overview

Arius is a tool that archives a local folder structure to/from Azure Blob Storage Archive Tier. The following diagram shows the concept of how Arius works.

![](docs/overview.png)

### Archive

Arius runs through the files of the (local) folder and subfolders.

For each file, it calculates the hash and checks whether (a **manifest** for) this hash already exists on blob storage.

If it does not exist, the local file is **chunk**ed (deduplicated). Each chunk is encrypted and uploaded to Archive storage. A **manifest** is then created, the list of chunks that make up the original file.

On the local file system, a **pointer file** is then created, pointing to the manifest.

For each pointer file, an entry is made in the Pointers table storage (containing relative name and manifest hash). This enables restoring the archive into an empty directory (by first reconstructing all the pointer files and then downloading and reconstituting all the chunks).

The result on the local file system looks like this:
![](docs/archive.png)

For a more detailed explanation, see [Developer Reference](#developer-reference).

### Restore

A restore consists out of two phases.

The first phase (optionally) synchonizes the pointer files in the local file system with the desired state, eg. restore into an empty folder, restore a previous version (point-in-time restore).

The second phase (also optionally) downloads the chunks and reassembles the original files.

For a more detailed explanation, see [Developer Reference](#developer-reference).

## Usage

### Archive to blob storage

#### CLI

```
arius archive
   --accountname <accountname>
  [--accountkey <accountkey>]
   --passphrase <passphrase>
  [--container <containername>]
  [--keep-local]
  [--tier=<hot/cool/archive>]
  [--min-size=<minsizeinMB>]
  [--simulate]
  <path>
```

#### Docker Run

```
docker run
  -v <path>:/archive
 [-v <logpath>:/log]
 [-e ARIUS_ACCOUNT_KEY=<accountkey>]
  ghcr.io/woutervanranst/arius:latest

  archive
   --accountname <accountname>
  [--accountkey <accountkey>]
   --passphrase <passphrase>
  [--container <containername>]
  [--keep-local]
  [--tier=<hot/cool/archive>]
  [--min-size=<minsizeinMB>]
  [--simulate]
```

#### Arguments

| Argument | Description | Notes |
| - | - | - |
| &#x2011;&#x2011;accountname, &#x2011;n | Storage Account Name
| &#x2011;&#x2011;accountkey, &#x2011;k | [Storage Account Key](https://docs.microsoft.com/en-us/azure/storage/common/storage-account-keys-manage?tabs=azure-portal) | Can be set through:<ul><li>Argument<li>Environment variable `ARIUS_ACCOUNT_KEY`<li>Docker environment variable `ARIUS_ACCOUNT_KEY`</ul>
| &#x2011;&#x2011;passphrase, &#x2011;p | Passphrase with which the blobs are encrypted
| &#x2011;&#x2011;container, &#x2011;c | Blob container to use | OPTIONAL. Default: 'arius'.
| &#x2011;&#x2011;keep-local | Do not delete the local files after archiving | OPTIONAL. Default: Local files are deleted after archiving.<br>NOTE: Setting this flag may result in long N+1 archive runs as all files need to be re-hashed.
| &#x2011;&#x2011;tier | Blob tier (hot/cool/archive) | OPTIONAL. Default: 'archive'.
| &#x2011;&#x2011;min&#x2011;size | Minimum size of files to archive (in MB) | OPTIONAL. Default: 0.<br>NOTE: when set to >0, a full restore will miss the smaller files
| path | Path to the folder to archive | <ul><li>CLI: argument `<path>`<li>Docker: as `-v <path>:/archive` volume argument</ul>
| logpath | Path to the folder to store the logs | NOTE: Only for Docker.

### Restore from blob storage

```
arius restore
   --accountname <accountname>
   --accountkey <accountkey>
   --passphrase <passphrase>
  [--container <containername>]
  [--synchronize]
  [--download]
  [--keep-pointers]
  path
```

If `<path>` is a Directory:

Synchronize the remote archive structure to the `<path>`:

- This command only touches the pointers (ie. `.arius` files). Other files are left untouched.
- Pointers that exist in the archive but not remote are created
- Pointers that exist locally but not in the archive are deleted

When the `--download` option is specified, the files are also downloaded WARNING this may consume a lot of bandwidth and may take a long time

If ``<path>`` is an `.arius` file `--download` flag is specified: the file is restored.

## Install

### Linux

Prerequisites:

- 7zip: `sudo apt-get install p7zip-full`
<!-- https://www.thomasmaurer.ch/2019/05/how-to-install-azcopy-for-azure-storage/ -->
- azcopy

```
wget https://aka.ms/downloadazcopy-v10-linux
tar -xvf downloadazcopy-v10-linux
sudo cp ./azcopy_linux_amd64_*/azcopy /usr/bin/
```

<!-- Install the latest linux Dapr CLI to `/usr/local/bin`

```bash
wget -q https://raw.githubusercontent.com/dapr/cli/master/install/install.sh -O - | /bin/bash -->

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

* AzCopy: https://docs.microsoft.com/en-us/azure/storage/common/storage-use-azcopy-v10#download-azcopy
* 7zip: https://www.7-zip.org/download.html

TODO

<!-- Install the latest windows Dapr CLI to `c:\dapr` and add this directory to User PATH environment variable. Use `-DaprRoot [path]` to change the default installation directory

```powershell
powershell -Command "iwr -useb https://raw.githubusercontent.com/dapr/cli/master/install/install.ps1 | iex"
https://chocolatey.org/install.ps1

```
-->

### Docker

```
docker pull ghcr.io/woutervanranst/arius
```

## Advanced

### Functional Flows

#### Archive

![Archive flow](https://lucid.app/publicSegments/view/52737a1c-52f2-4f03-8c70-6ee6cdaab8c0/image.png)

#### Restore

![Restore flow](https://lucid.app/publicSegments/view/86952f67-e660-44d3-b467-9c84f811f3d1/image.png)

### Restore with common tools

Arius relies on the 7zip command line and Azure blob storage cli.

### Deduplication

A 1 GB file chunked into chunks of 64 KB, with each chunk having a SHA256 hash (32 bytes = 64 hex characters) * 4 bytes/UTF8 character = 4 MB of manifest

((1 GB) / (64 KB)) * (64 * 4 bytes) = 4 megabytes

## Developer reference

### Flow Walkthrough

#### Archive

Consider the following example directory: three files of which two are a duplicate.
Running `arius archive` on a local folder will yield the following:

![](docs/archive.png)

Arius creates pointer files (ending in .arius.pointer) that reflect the original file/folder structure, have the same name and dates as the original file, yet only 1KB in size.

The original files can now be deleted. _NOTE: Not specifying `--keep-local` will delete the original files by default after a successful archive._

The contents of the pointer files are as follows:

![](docs/after_archive_withpointers.png)

Note that the duplicate files (ie. 'Git-2.29.2.2-64-bit.exe' and 'Git-2.29.2.2-64-bit (1).exe') have the same hash and that the pointers thus point to the same manifest.

The contents of the manifest container are:

![alt](docs/pointers_with_manifests.png)

Note that there are only two manifests.

The contents of the first manifest (after decryption) are:

![alt](docs/unzipped_manifest.png)

The structure of the manifest is as follows:
- PointerFileEntries: the list of pointers pointing to this manifest. From this list the `restore` operation can reconstitute the original file/folder structure.
  - RelativeName: path relative to the root of the folder that was archived
  - Version: date & time at which the local file system contained this entry. Multiple entries can exist for one RelativeName, eg when LastWriteTime is modified or the file is deleted. The `restore` operation takes the last version when restoring. Optionally, for point-in-time restores, this field is used to determine the files to restore.
  - IsDeleted: flag marking the file existed once but is now deleted.
  - CreationTimeUtc, LastWriteTimeUtc: respective properties from the original file. Used when deciding to make a new version of the entry.
- ChunkNames: list of the chunks that make up the original file.
- Hash: the SHA256 hash of the original file.

NOTE: since this file consists of only one chunk, the hash of the chunk and the hash of the original file are the same.


#### Debugging Docker in Visual Studio

| Argument | Visual Studio Debug |
| - | - |
| ``--accountname`` | argument in ``commandLineArgs`` in ``launchSettings.json`` |
| ``--accountkey`` | <ul><li>argument in ``commandLineArgs`` in ``launchSettings.json`` (but it would be in source control)</li><li>Environment Variable (``%ARIUS_ACCOUNT_KEY%``) &rarr; <br> Pre-build event in Arius.csproj &rarr; <br> ``<DockerfileRunEnvironmentFiles>``</li>
| ``--passphrase`` | argument  in ``commandLineArgs`` in ``launchSettings.json`` |
| ``(--container)`` | argument  in ``commandLineArgs`` in ``launchSettings.json`` |
| ``(--keep-local)`` | argument in ``commandLineArgs`` in ``launchSettings.json`` |
| ``(--tier)`` | argument in ``commandLineArgs`` in ``launchSettings.json`` |
| ``(--min-size)`` | argument in ``commandLineArgs`` in ``launchSettings.json`` |
| ``(--simulate)``  | argument in ``commandLineArgs`` in ``launchSettings.json`` |
| ``<path>``  | ``<DockerfileRunArguments>`` in ``Arius.csproj``, eg.<br> ``-v "c:\Users\Wouter\Documents\Test:/archive"`


# Attributions

Arius Icon by [Freepik](https://www.flaticon.com/free-icon/iceberg_2055379?related_id=2055379).