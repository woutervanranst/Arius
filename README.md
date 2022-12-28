# Arius

<img src="docs/iceberg.svg" width="200" />

[![Arius.Cli - Docker](https://github.com/woutervanranst/Arius/actions/workflows/cli-release-docker.yml/badge.svg)](https://github.com/woutervanranst/Arius/actions/workflows/cli-release-docker.yml)

[![Arius.Core - Nuget Package](https://github.com/woutervanranst/Arius/actions/workflows/core-release-nuget.yml/badge.svg)](https://github.com/woutervanranst/Arius/actions/workflows/core-release-nuget.yml)

Arius is a lightweight tiered archival solution, specifically built to leverage the Azure Blob Archive tier.

The name derives from the Greek for 'immortal'.

- [Arius](#arius)
  - [Key design scenarios](#key-design-scenarios)
  - [Key design objectives](#key-design-objectives)
  - [Usage](#usage)
    - [Archive to blob storage](#archive-to-blob-storage)
      - [CLI](#cli)
      - [Docker](#docker)
    - [Restore from blob storage](#restore-from-blob-storage)
      - [CLI](#cli-1)
      - [Docker](#docker-1)
    - [Arguments](#arguments)
  - [Installing](#installing)
    - [Docker (Recommended option)](#docker-recommended-option)
    - [Linux (CLI)](#linux-cli)
    - [Windows (CLI)](#windows-cli)
    - [Windows GUI](#windows-gui)
    - [Restore manually](#restore-manually)
      - [Getting the correct binary](#getting-the-correct-binary)
      - [Decrypt and unpack](#decrypt-and-unpack)
- [Attributions](#attributions)

## Key design scenarios

Why Arius?

**Scenario 1: support 3-2-1 backup strategy**

- I keep my backups on offline disks but want a secure and cheap offsite backup.
- With Arius and the blob archive tier, the cost is approx. 1 EUR per TB per month.

**Scenario 2: single pane of glass of all files**

- I do not want to open a separate application to see what is in my offline backups.
- Arius creates 'pointers' of <1KB each on the local hard drive. That way, the Search in Windows Explorer makes them visible.

**Scenario 3: encryption**

- I want client side encryption (because of \<reasons>).
- Arius uses AES256 / openssl compatible encryption.

**Scenario 4: deduplication**

- I have OCD and do not want to store duplicate files/parts of files twice.
- Arius deduplicates on file level by default, and can optionally 'chunk' files into multiple parts and deduplicate on these.

## Key design objectives

- [x] Maintain the local file structure (files/folders) by creating 'sparse' placeholders (Scenario 2).
- [x] Files, folders & filenames are encrypted clientside (Scenario 3).
- [x] The local filestructure is _not_ reflected in the archive structure (ie it is obfuscated) (Scenario 3).
- [x] Changes in the local file _structure_ do not cause a reshuffle in the archive (which doesn't sit well with Archive storage).
- [x] Never delete files on remote.
- [x] No central store to avoid a single point of failure.
- [x] File level deduplication (Scenario 4).
- [x] Variable block size deduplication (Scenario 4).
- [x] Leverage common tools, to allow restores even when this project would become deprecated.
- [ ] Point in time restore (FUTURE).

## Usage

### Archive to blob storage

#### CLI

```
arius archive <path>
   --accountname <accountname>
   --accountkey <accountkey>
   --passphrase <passphrase>
   --container <containername>
  [--remove-local]
  [--tier=<hot/cool/archive>]
  [--dedup]
  [--fasthash]
```

#### Docker

```
docker run
  -v <absolute_path_to_archive>:/archive
 [-v <absolute_path_to_logs>:/logs]
  ghcr.io/woutervanranst/arius:latest

  archive
   --accountname <accountname>
   --accountkey <accountkey>
   --passphrase <passphrase>
   --container <containername>
  [--remove-local]
  [--tier=<hot/cool/archive>]
  [--dedup]
  [--fasthash]
```

### Restore from blob storage

#### CLI

```
arius restore <path>
   --accountname <accountname>
   --accountkey <accountkey>
   --passphrase <passphrase>
   --container <containername>
  [--synchronize]
  [--download]
  [--keep-pointers]
  
```

#### Docker

```
docker run
  -v <absolute_path_to_archive>:/archive
 [-v <absolute_path_to_logs>:/logs]
  ghcr.io/woutervanranst/arius:latest

  restore
   --accountname <accountname>
   --accountkey <accountkey>
   --passphrase <passphrase>
   --container <containername>
  [--synchronize]
  [--download]
  [--keep-pointers]
```

### Arguments

| Argument | Description | Notes |
| - | - | - |
| path | The path on the local file system | For `archive`:<br>The root directory to archive<br><br>For `restore`:<ul><li>If path is a directory: restore all pointer files in the (sub)directories.<li>If path is a file: restore this file.</ul>
| logpath | Path to the folder to store the logs | OPTIONAL. NOTE: Only for Docker.
| &#x2011;&#x2011;accountname, &#x2011;n | Storage Account Name
| &#x2011;&#x2011;accountkey, &#x2011;k | [Storage Account Key](https://docs.microsoft.com/en-us/azure/storage/common/storage-account-keys-manage?tabs=azure-portal) | Can be set through:<ul><li>Argument<li>Environment variable `ARIUS_ACCOUNT_KEY`<li>Docker environment variable `ARIUS_ACCOUNT_KEY`</ul>
| &#x2011;&#x2011;passphrase, &#x2011;p | Passphrase with which the blobs are encrypted
| &#x2011;&#x2011;container, &#x2011;c | Blob container to use | OPTIONAL. Default: 'arius'.
| &#x2011;&#x2011;remove-local | Remove local file after a successful upload | `archive`-only<br> OPTIONAL. Default: Local files are not deleted after archiving.
| &#x2011;&#x2011;tier | [Blob storage tier (hot/cool/archive)](https://docs.microsoft.com/en-us/azure/storage/blobs/access-tiers-overview) | `archive`-only<br> OPTIONAL. Default: 'archive'.
| &#x2011;&#x2011;dedup | Deduplicate on block level | `archive`-only<br> OPTIONAL. Default: deduplicate on file level.
| &#x2011;&#x2011;fasthash | When a pointer file is present, use that hash instead of re-hashing the full file again | `archive`-only<br> OPTIONAL. Default: false.<br>NOTE: Do **NOT** use this if the contents of the files are modified. Arius will not pick up the changes.
| &#x2011;&#x2011;synchronize | Bring the structure of the local file system (pointer files) in line with the latest state of the remote repository | `restore`-only<br> OPTIONAL. Default: do not synchronize.<br>This command only touches the pointers (ie. `.pointer.arius` files). Other files are left untouched:<ul><li>Pointers that exist in the archive but not locally are created.<li>Pointers that exist locally but not in the archive are deleted</ul>
| &#x2011;&#x2011;download | Download and restore the actual file (contents) |  `restore`-only<br> OPTIONAL. Default: do not download.<br>NOTE: If the file is in the archive blob tier, hydration to an online tier is started. Run the restore command again after ~15 hours to download the file.
| &#x2011;&#x2011;keep-pointers | Keep pointer files after downloading content files | `restore`-only<br>OPTIONAL. Default: keep the pointers. 


## Installing

### Docker (Recommended option)

```
docker pull ghcr.io/woutervanranst/arius
```

### Linux (CLI)

Execute the `install.sh` file in the install folder.

```
dotnet arius.dll <see syntax above>
```

### Windows (CLI)

TODO

<!-- Install the latest windows Dapr CLI to `c:\dapr` and add this directory to User PATH environment variable. Use `-DaprRoot [path]` to change the default installation directory

```powershell
powershell -Command "iwr -useb https://raw.githubusercontent.com/dapr/cli/master/install/install.ps1 | iex"
https://chocolatey.org/install.ps1

```
-->

### Windows GUI

TODO

### Restore manually

#### Getting the correct binary

1. Open the `.arius.pointer` file (with Notepad) and look for the `BinaryHash` value.
1. Using Azure Storage Explorer, navigate to the correct container in the storage account and, in the `chunks` folder, locate the blob with the maching name.
`. Decrypt and unpack using the below steps

If the pointer is not available locally:
1. Download the most recent file in the `states` folder in the storage account`
1. Decrypt and unpack using the below steps
1. Using `DB Browser for SQLite`, navigate to the `PointerFileEntries` table, filter on `RelativeName` to find the correct `BinaryHash`
`. Proceed as above

If you cannot locate a chunk with matching `BinaryHash` value or arius was run with the `--dedup` option:

1. In the `chunklist` folder, download the blob with matching value
1. Open the file with Notepad
1. Download, decrypt and unpack each of the chunks as listed in the file
1. Concatenate the chunks (using `cat chunk1 chunk2 chunk3 ... > original.file` (Linux) or `copy chunk1 + chunk2 + chunk3 + ... > original.file` (Windows)) 

#### Decrypt and unpack

Arius files are gzip'ped and then encrypted with AES256. To decrypt:

```
# 1. Decrypt with OpenSSL
openssl enc -d -aes-256-cbc -in $ENCRYPTED_FILE -out original.file.gz -pass pass:$PASSPHRASE -pbkdf2

# 2. Unpack
gzip -d original.file.gz -f

# 3. at this point 'original.file' will be the original binary
```


# Attributions

Arius Icon by [Freepik](https://www.flaticon.com/free-icon/iceberg_2055379?related_id=2055379).