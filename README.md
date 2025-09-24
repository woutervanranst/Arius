# Arius: a Lightweight Tiered Archival Solution for Azure Blob Storage

<img src="docs/iceberg.svg" width="200" />

[![Arius.Core - Test Suite](https://github.com/woutervanranst/Arius/actions/workflows/core-test.yml/badge.svg)](https://github.com/woutervanranst/Arius/actions/workflows/core-test.yml)
[![codecov](https://codecov.io/gh/woutervanranst/Arius/graph/badge.svg?token=GEZJ4Y0ZNK)](https://codecov.io/gh/woutervanranst/Arius)
[![CodeQL: Arius.Core](https://github.com/woutervanranst/Arius/actions/workflows/codeql-analysis.yml/badge.svg)](https://github.com/woutervanranst/Arius/actions/workflows/codeql-analysis.yml)

[![Arius Release](https://github.com/woutervanranst/Arius/actions/workflows/release.yml/badge.svg)](https://github.com/woutervanranst/Arius/actions/workflows/release.yml)
[![Docker](https://img.shields.io/docker/v/woutervanranst/arius?logo=docker&label=Docker)](https://hub.docker.com/r/woutervanranst/arius)
[![Arius.Core Version](https://img.shields.io/nuget/v/WouterVanRanst.Arius.Core?logo=nuget)](https://www.nuget.org/packages/WouterVanRanst.Arius.Core)
[![ClickOnce](https://img.shields.io/badge/Windows-ClickOnce-dsfs?logo=windows&logoColor=lightblue)](https://woutervanranst.github.io/Arius/explorer/Arius.Explorer.application)

Arius is a cross-platform archival solution that keeps cold backups affordable without giving up visibility into your files. It encrypts and deduplicates data locally, stores the content in Azure Blob Storage (Archive/Cool/Cold/Hot tiers), and leaves *pointer* files behind so your backup still looks like a regular folder structure.

---

- [Key features](#key-features)
- [Quick start](#quick-start)
  - [Prerequisites](#prerequisites)
  - [Get the CLI](#get-the-cli)
  - [Configure Azure Storage](#configure-azure-storage)
  - [Archive data](#archive-data)
  - [Restore data](#restore-data)
- [Command reference](#command-reference)
  - [Archive](#archive)
  - [Restore](#restore)
  - [Running in Docker](#running-in-docker)
- [Arius Explorer (Windows UI)](#arius-explorer-windows-ui)
- [Documentation](#documentation)
- [Manual disaster recovery](#manual-disaster-recovery)
- [Architecture](#architecture)

## Key features

* **3-2-1 friendly off-site backups** – Archive cold data into low-cost Azure Blob tiers while keeping your production storage clean.
* **Client-side encryption** – AES-256 encryption happens before anything leaves your machine; only encrypted blobs reach Azure.
* **Chunk-level deduplication** – Identical content is stored only once, even when filenames or locations differ.
* **Pointer-aware file system integration** – Tiny `.arius.pointer` files keep your directory tree browsable and searchable.
* **Automatable CLI & Docker support** – Run Arius interactively, on a schedule, or in containers as part of your backup pipeline.

![](docs/overview.png)

## Quick start

### Prerequisites

1. [.NET SDK 9.0 or later](https://dotnet.microsoft.com/download) (for running or building the CLI locally).
2. An Azure Storage account with [hierarchical namespace disabled](https://learn.microsoft.com/azure/storage/blobs/data-lake-storage-namespace) and the Archive tier available.
3. A secure passphrase that will be used to encrypt and decrypt your blobs.

### Get the CLI

* **Download a build:** Pre-built binaries for Linux, macOS, and Windows are published on the [GitHub Releases](https://github.com/woutervanranst/Arius/releases) page.
* **Build from source:**
  ```bash
  git clone https://github.com/woutervanranst/Arius.git
  cd Arius
  dotnet publish src/Arius.Cli -c Release -r linux-x64 --self-contained false -o ./publish
  ./publish/arius --version
  ```
  Adjust `-r` for your OS (`win-x64`, `osx-arm64`, ...). Replace `--self-contained false` with `true` if you want a single-file build that includes the runtime.

### Configure Azure Storage

1. Create (or reuse) a blob container that will store Arius data.
2. Generate a storage account access key (Key 1 or Key 2 in the Azure Portal).
3. Optionally set environment variables so you don't have to pass credentials on every run:
   ```bash
   export ARIUS_ACCOUNT_NAME=<storage-account-name>
   export ARIUS_ACCOUNT_KEY=<storage-account-key>
   ```

### Archive data

```bash
arius archive /path/to/data \
  --accountname <storage-account-name> \
  --accountkey <storage-account-key> \
  --container <container-name> \
  --passphrase <encryption-passphrase>
```

During the run Arius will:

* hash and deduplicate data
* compress and encrypt before upload
* write progress updates to the console
* leave `.arius.pointer` files in place of archived files (unless `--remove-local` is specified)

Logs are written to `%LOCALAPPDATA%/Arius/logs` on Windows and `~/.local/share/Arius/logs` on Linux/macOS. When running in Docker the logs are written to `/logs`.

### Restore data

```bash
arius restore --root /restore/location "Photos/2020" "Documents/report.docx" \
  --accountname <storage-account-name> \
  --accountkey <storage-account-key> \
  --container <container-name> \
  --passphrase <encryption-passphrase> \
  --download --include-pointers
```

* `--download` hydrates blobs from Azure and writes the binary content locally.
* `--include-pointers` recreates pointer files next to restored binaries so the folder stays "Arius-aware".
* Omitting `--download` performs a *sync-only* run that updates/creates pointer files without pulling full data.

## Command reference

### Archive

| Parameter | Description | Notes |
| --- | --- | --- |
| `<local-root>` | Directory to archive. | A pointer file replaces each archived file unless `--remove-local` is used. |
| `--accountname`, `-n` | Azure Storage account name. | Can also be supplied via `ARIUS_ACCOUNT_NAME`. |
| `--accountkey`, `-k` | Azure Storage account key. | Can also be supplied via `ARIUS_ACCOUNT_KEY`. |
| `--container`, `-c` | Blob container that will hold chunks, pointers, and state. | Required. |
| `--passphrase`, `-p` | Encryption passphrase. | Required – keep it safe. |
| `--tier` | Storage tier for uploaded chunks. | Defaults to `archive`. Supported values: `hot`, `cool`, `cold`, `archive`. |
| `--remove-local` | Delete local binaries after successful upload. | Pointer files remain so the folder stays browsable. |

### Restore

| Parameter | Description | Notes |
| --- | --- | --- |
| `--root`, `-r` | Local root folder to use for restores. | Defaults to the current working directory. Required when running from outside the target folder. |
| `Targets` | Optional paths (files or directories) under the root to restore. | Defaults to `./` (entire repository). |
| `--accountname`, `-n` | Azure Storage account name. | Supports `ARIUS_ACCOUNT_NAME`. |
| `--accountkey`, `-k` | Azure Storage account key. | Supports `ARIUS_ACCOUNT_KEY`. |
| `--container`, `-c` | Azure Blob container to read from. | Required. |
| `--passphrase`, `-p` | Passphrase used during archive. | Required. |
| `--download` | Retrieve the binary content. | Without this flag the command only syncs pointer files. |
| `--include-pointers` | Keep or recreate pointer files alongside restored binaries. | Recommended for ongoing Arius use. |

### Running in Docker

The official container image (`woutervanranst/arius`) contains the CLI and expects bind mounts for both your data and optional logs.

```bash
docker run --rm \
  -v /absolute/path/to/data:/archive \
  -v /absolute/path/to/logs:/logs \
  woutervanranst/arius archive \
    --accountname <storage-account-name> \
    --accountkey <storage-account-key> \
    --container <container-name> \
    --passphrase <passphrase>
```

For restore runs, replace `archive` with `restore` and add `--download` / `--include-pointers` as needed. The container always works inside `/archive`, so you do not pass a positional path parameter.

## Arius Explorer (Windows UI)

![](docs/arius.explorer.png)

Arius Explorer provides a Windows desktop experience on top of the same repository:

1. Install it via the [ClickOnce installer](https://woutervanranst.github.io/Arius/Arius.Explorer.application).
2. Sign in with the same storage account, container, and passphrase that you use for the CLI.
3. Browse pointer status – icons combine local state (left half) and cloud state (right half).

| Icon | Local file system | Azure repository |
| --- | --- | --- |
| ![](docs/status/NNYC.png) | Not present | Pointer and binary |
| ![](docs/status/NYYC.png) | Pointer only | Pointer and binary |
| ![](docs/status/YYYC.png) | Pointer and binary | Pointer and binary |

## Documentation

Additional technical documentation lives in the [`docs/`](docs) folder:

* [Archive command deep dive](docs/ArchiveCommand.md)
* [Storage abstractions](docs/Storage.md)
* [Versioning model](docs/versioning.md)
* [State repository internals](docs/StateRepository.md)

## Manual disaster recovery

Need to recover data without Arius? Follow the step-by-step [manual restore guide](docs/manualrestore.md) to decrypt blobs and recreate files directly from Azure Storage Explorer.

## Architecture

### Dependencies

![](http://www.plantuml.com/plantuml/proxy?cache=no&src=https://raw.githubusercontent.com/woutervanranst/Arius/main/docs/dependencies.puml)

### Arius.Core architecture

![](docs/AriusFlows-Arius.Core%20Structure.drawio.svg)
