# Arius

![Arius.Tests](https://github.com/woutervanranst/Arius/workflows/Arius.Tests/badge.svg)

Arius is a lightweight tiered archival solution, specifically built to leverage the Azure Blob Archive tier.

The name derives from the Greek for 'immortal'.

- [Arius](#arius)
  - [Key design objectives](#key-design-objectives)
  - [Usage](#usage)
    - [Archive to blob storage](#archive-to-blob-storage)
    - [Restore from blob storage](#restore-from-blob-storage)
  - [Install](#install)
    - [Linux](#linux)
    - [Windows](#windows)
    - [Docker](#docker)
      - [Archive](#archive)
      - [Example Build Command](#example-build-command)
      - [Example Run Command](#example-run-command)
      - [Cleanup](#cleanup)
  - [Advanced](#advanced)
    - [Restore with common tools](#restore-with-common-tools)

## Key design objectives

* Local file structure (files/folders) by creating 'sparse' placeholders
* Files, folders & filenames are encrypted clientside
* The local filestructure is _not_ reflected in the archive structure (ie it is obfuscated)
* Changes in the local file _structure_ do not cause a reshuffle in the archive (which doesn't sit well with Archive storage)
* Never delete files on remote
* Point in time restore (FUTURE)
* No central store to avoid a single point of failure
* File level deduplication, optionally variable block size (rolling hash Rabin-Karp) deduplication
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
  (--keep-pointers)
  <path>
```

If `<path>` is a Directory:

Synchronize the remote archive structure to the `<path>`:

* This command only touches the pointers (ie. `.arius` files). Other files are left untouched.
* Pointers that exist in the archive but not remote are created
* Pointers that exist locally but not in the archive are deleted

When the `--download` option is specified, the files are also downloaded WARNING this may consume a lot of bandwidth and may take a long time

If ``<path>`` is an `.arius` file `--download` flag is specified: the file is restored.

## Install

### Linux

Prerequisites:

* 7zip: `sudo apt-get install p7zip-full`
<!-- https://www.thomasmaurer.ch/2019/05/how-to-install-azcopy-for-azure-storage/ -->
* azcopy 
```
wget https://aka.ms/downloadazcopy-v10-linux
tar -xvf downloadazcopy-v10-linux
sudo cp ./azcopy_linux_amd64_*/azcopy /usr/bin/
```

* 

Install the latest linux Dapr CLI to `/usr/local/bin`

```bash
wget -q https://raw.githubusercontent.com/dapr/cli/master/install/install.sh -O - | /bin/bash

Run the following commands:
<!-- from https://blog.markvincze.com/download-artifacts-from-a-latest-github-release-in-sh-and-powershell/ -->

LATEST_RELEASE=$(curl -L -s -H 'Accept: application/json' https://github.com/woutervanranst/arius/releases/latest)
LATEST_VERSION=$(echo $LATEST_RELEASE | sed -e 's/.*"tag_name":"\([^"]*\)".*/\1/')
ARTIFACT_URL="https://github.com/woutervanranst/arius/releases/download/$LATEST_VERSION/release.zip"
wget $ARTIFACT_URL
unzip release.zip
dotnet arius.dll ...
```

### Windows

Install the latest windows Dapr CLI to `c:\dapr` and add this directory to User PATH environment variable. Use `-DaprRoot [path]` to change the default installation directory

```powershell
powershell -Command "iwr -useb https://raw.githubusercontent.com/dapr/cli/master/install/install.ps1 | iex"
```

### Docker

#### Archive

| ``arius archive``  | Visual Studio Debug | ``Docker Run`` |
|---|---|---|
| ``--accountname`` | argument in ``commandLineArgs`` in ``launchSettings.json`` | argument |
| ``--accountkey`` | <ul><li>argument in ``commandLineArgs`` in ``launchSettings.json`` (but it would be in source control)</li><li>Environment Variable (``%ARIUS_ACCOUNT_KEY%``) &rarr; <br> Pre-build event in Arius.csproj &rarr; <br> ``<DockerfileRunEnvironmentFiles>``</li> | <ul><li>argument</li>  <li>environment argument, eg. <br> ``-e ARIUS_ACCOUNT_KEY=%ARIUS_ACCOUNT_KEY%``</li></ul> |
| ``--passphrase`` | argument  in ``commandLineArgs`` in ``launchSettings.json`` | argument |
| ``(--container)`` | argument  in ``commandLineArgs`` in ``launchSettings.json`` | argument |
| ``(--keep-local)`` | argument in ``commandLineArgs`` in ``launchSettings.json`` | argument |
| ``(--tier)`` | argument in ``commandLineArgs`` in ``launchSettings.json`` | argument |
| ``(--min-size)`` | argument in ``commandLineArgs`` in ``launchSettings.json`` | argument |
| ``(--simulate)``  | argument in ``commandLineArgs`` in ``launchSettings.json`` |  argument |
| ``<path>``  | ``<DockerfileRunArguments>`` in ``Arius.csproj``, eg.<br> ``-v "c:\Users\Wouter\Documents\Test:/archive"``  | volume argument, eg. <br> ``-v c:/Users/Wouter/Documents/Test:/archive`` |

#### Example Build Command

<!-- 
cd C:\Users\Wouter\Documents\GitHub\Arius\Arius\Arius 
-->

```
docker build -f Dockerfile .. -t arius:prd
```

#### Example Run Command

```
docker run -e ARIUS_ACCOUNT_KEY=%ARIUS_ACCOUNT_KEY% -v c:/Users/Wouter/Documents/Test:/archive arius:prd archive -n aurius -p woutervr
```

#### Cleanup

```
docker container prune -f
docker image prune -f
```

## Advanced

### Restore with common tools

Arius relies on the 7zip command line and Azure blob storage cli.
