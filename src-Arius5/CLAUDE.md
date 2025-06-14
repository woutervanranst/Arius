# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Arius is a file archival system that encrypts, compresses, and uploads files to Azure Blob Storage. The solution consists of three projects:

- **Arius.Core**: Core business logic and domain models
- **Arius.Cli**: Console application for running archive operations
- **Arius.Core.Tests**: xUnit test project

## Architecture

The codebase follows a **CQRS pattern** using MediatR for command handling. The main workflow is implemented in `ArchiveCommandHandler` which:

1. **Indexes** files from a local directory using the Zio filesystem abstraction
2. **Hashes** files in parallel using SHA256
3. **Uploads** files to Azure Blob Storage with AES256 encryption and gzip compression
4. **Creates pointer files** (.pointer.arius) that reference the uploaded content by hash
5. **Manages state** using SQLite with Entity Framework Core

### Key Components

- **FilePair**: Core domain model representing binary files and their corresponding pointer files
- **Hash**: Immutable value object for SHA256 hashes with implicit conversions
- **BlobStorage**: Azure Blob Storage client wrapper
- **FilePairFileSystem**: Zio-based filesystem abstraction for handling file pairs
- **StateRepository**: SQLite-based persistence layer for tracking uploaded files

### File Processing Strategy

- **Large files** (>2MB): Uploaded individually with parallel processing
- **Small files** (≤2MB): Batched into TAR archives before upload to optimize costs
- **Deduplication**: Files with identical hashes are uploaded only once

## Development Commands

### Build
```bash
dotnet build
```

### Run Tests
```bash
dotnet test
```

### Run a Single Test Project
```bash
dotnet test Arius.Core.Tests
```

### Run CLI Application
```bash
dotnet run --project Arius.Cli
```

## Configuration

The CLI application requires configuration via user secrets:
- `ArchiveSettings:AccountName`: Azure Storage account name
- `ArchiveSettings:AccountKey`: Azure Storage account key
- `ArchiveSettings:ContainerName`: Blob container name
- `ArchiveSettings:Passphrase`: Encryption passphrase
- `ArchiveSettings:LocalRoot`: Local directory to archive
- `ArchiveSettings:RemoveLocal`: Whether to remove local files after archiving
- `ArchiveSettings:Tier`: Storage tier (Hot, Cool, Archive)

## Key Dependencies

- **MediatR**: CQRS command/query handling
- **Zio**: Cross-platform filesystem abstraction
- **Azure.Storage.Blobs**: Azure Blob Storage client
- **Entity Framework Core**: SQLite persistence
- **Spectre.Console**: Rich console UI with progress reporting
- **xUnit**: Test framework with FluentAssertions and NSubstitute