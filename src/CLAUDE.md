# CLAUDE.md

## Project Overview

Arius is a lightweight tiered archival solution for Azure Blob Storage. 

## Build and Test Commands
! You are running in WSL, but need to build/test in Windows. Therefore, use the ./build-wsl.sh script.

```bash
# Build the entire solution
./build-wsl.sh build Arius.sln

# Run all tests
./build-wsl.sh test Arius.sln

# Run a single test
./build-wsl.sh test Arius.sln --filter "FullyQualifiedName~<TestClassName>.<TestMethodName>"
```

## Running the Application

```bash
# Run CLI application
cd Arius.Cli
dotnet run -- [command] [options]

# Archive command example
dotnet run -- archive <path> --accountname <name> --accountkey <key> --passphrase <pass> --container <container>

# Restore command example
dotnet run -- restore <path> --accountname <name> --accountkey <key> --passphrase <pass> --container <container>

# Run WPF Explorer application
cd Arius.Explorer
dotnet run
```

## Architecture

The solution follows CQRS pattern with Mediator, using Domain-Driven Design principles:
- **Commands flow**: CLI/WPF → Mediator → Command Handlers → Services/Repositories → Azure Storage

Projects:
- **Arius.Explorer**: WPF application providing graphical interface for repository exploration and file management
- **Arius.Cli**: CLI application using CliFx framework with CliCommands that dispatch to Core handlers
- **Arius.Core**: Core business logic using vertical slice architecture with Features/Commands and Features/Queries, where commands and handlers are co-located by feature

Key architectural decisions:
- File-level deduplication for storage optimization
- Client-side AES256 encryption for security
- Pointer files (.pointer.arius) maintain local filesystem visibility while actual data resides in Azure
- Entity Framework Core with SQLite for local metadata storage

### Key Concepts

- **FilePair**: Core domain model representing binary files and their corresponding pointer files
- **Hash**: Immutable value object for SHA256 hashes with implicit conversions
- **BlobStorage**: Azure Blob Storage client wrapper
- **FilePairFileSystem**: Zio-based filesystem abstraction for handling file pairs
- **StateRepository**: SQLite-based persistence layer for tracking uploaded files

## Development Configuration

The projects use:
- **.NET 9.0** as target framework
- **Central Package Management** via Directory.Packages.props
- **User Secrets** for local development
- **Dependency Injection** configured in Program.cs

Required environment variables for integration tests:
- `ARIUS_ACCOUNT_NAME`
- `ARIUS_ACCOUNT_KEY`
- `RepositoryOptions__AccountKey`
- `RepositoryOptions__Passphrase`

## Code Patterns

When implementing new features:
1. Create command/query in Arius.Core/Features/Commands or Features/Queries
2. Implement handler using IRequestHandler<TCommand, TResult> in the same feature folder
3. Add CLI command in Arius.Cli/CliCommands inheriting from ICommand
4. Use IMediator to dispatch from CLI to Core handler
5. Write unit tests in corresponding .Tests projects using xUnit, NSubstitute, and Shouldly

## Key Dependencies

### Arius.Cli
- **CliFx**: Command-line interface framework
- **Spectre.Console**: Enhanced console output

### Arius.Explorer (WPF)
- **CommunityToolkit.Mvvm**: MVVM framework for WPF applications
### Arius.Core
- **Azure.Storage.Blobs**: Azure Blob Storage integration
- **Mediator**: CQRS implementation
- **Zio**: File system abstraction (only used in Arius.Core)
- **FluentValidation**: Command validation
### All
- **Serilog**: Structured logging
- **WouterVanRanst.Utils**: Utility library for common extensions
- **xUnit, NSubstitute, Shouldly**: Testing stack

## Development Notes

- Add FluentValidation to validate the commands
- For unit tests, reuse the existing Fixtures
- You do NOT need to take care of backwards compatibility
- Naming convention for local fields is camelCase (without leading _)

## Code Style Preferences

- Do not use the XUnit assertions. Use Shouldy whenever possible.
- Use FakeLogger instead of NullLogger

### Helper Methods
Prefer **local methods** over private static methods for helper functionality that is only used within a single method