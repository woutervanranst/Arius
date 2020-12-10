# Arius

![Arius.Tests](https://github.com/woutervanranst/Arius/workflows/Arius.Tests/badge.svg)

Arius is a lightweight tiered archival solution, specifically built to leverage the Azure Blob Archive tier.

The name derives from the Greek for 'immortal'.

- [Arius](#arius)
  - [Key design objectives](#key-design-objectives)
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
    - [Restore with common tools](#restore-with-common-tools)
  - [Developer reference](#developer-reference)
    - [Terminilogy](#terminilogy)
    - [Functional Flows](#functional-flows)
      - [Archive](#archive)
    - [Docker](#docker-1)
      - [Docker Build](#docker-build)
      - [Debuuging Docker in Visual Studio](#debuuging-docker-in-visual-studio)

## Key design objectives

- Local file structure (files/folders) by creating 'sparse' placeholders
- Files, folders & filenames are encrypted clientside
- The local filestructure is _not_ reflected in the archive structure (ie it is obfuscated)
- Changes in the local file _structure_ do not cause a reshuffle in the archive (which doesn't sit well with Archive storage)
- Never delete files on remote
- Point in time restore (FUTURE)
- No central store to avoid a single point of failure
- File level deduplication, optionally variable block size (rolling hash Rabin-Karp) deduplication
- Leverage common tools, to allow restores even when this project would become deprecated

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

### Restore with common tools

Arius relies on the 7zip command line and Azure blob storage cli.

## Developer reference

### Terminilogy

- Manifest
- Pointer
- Chunk

### Functional Flows

#### Archive

![alt](docs/archive_flow.png)

### Docker

#### Docker Build

<!-- 
cd C:\Users\Wouter\Documents\GitHub\Arius\Arius\Arius 
-->

```
docker build -f Dockerfile .. -t arius:prd
```

#### Debuuging Docker in Visual Studio

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
