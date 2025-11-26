# Arius Repository Structure

```
Arius/
│
├── .github/                    # GitHub workflows and CI/CD
├── .serena/                    # Serena MCP server configuration
│
├── docs/                       # Documentation
│   ├── ArchiveCommand.md
│   ├── StateRepository.md
│   ├── Storage.md
│   ├── versioning.md
│   └── ...
│
└── src/                        # Source code root
    │
    ├── Arius.sln                       # Solution file
    ├── Directory.Packages.props         # Central package management
    │
    ├── 📦 Main Projects
    │   ├── Arius.Explorer/             # 🖥️  WPF Desktop Application
    │   ├── Arius.Cli/                  # ⌨️  Command Line Interface
    │   └── Arius.Core/                 # 🧠 Core Business Logic (CQRS/Mediator)
    │
    ├── 🧪 Test Projects
    │   ├── Arius.Explorer.Tests/       # Explorer unit tests
    │   ├── Arius.Cli.Tests/            # CLI unit tests
    │   ├── Arius.Core.Tests/           # Core unit tests
    │   └── Arius.Core.BehaviorTests/   # Core integration/behavior tests
    │
    ├── 🔧 Utility Projects
    │   ├── Arius.Benchmarks/           # Performance benchmarks
    │   ├── Arius.Core.DbMigrationV2V3/ # Database migration tool (v2→v3)
    │   ├── Arius.Core.DbMigrationV3V5/ # Database migration tool (v3→v5)
    │   ├── Arius.UI/                   # Shared UI components
    │   └── Arius.UI.Tests/             # UI component tests
    │
    └── build-wsl.sh                    # WSL build script wrapper
```

## Project Dependencies

```
┌─────────────────────┐
│  Arius.Explorer     │ ──┐
│  (WPF Application)  │   │
└─────────────────────┘   │
                          │
┌─────────────────────┐   │       ┌──────────────────────┐
│   Arius.Cli         │ ──┼──────▶│   Arius.Core         │
│  (CLI Application)  │   │       │  (Business Logic)    │
└─────────────────────┘   │       │  - Commands          │
                          │       │  - Queries           │
┌─────────────────────┐   │       │  - Domain Models     │
│   Arius.UI          │ ──┘       │  - Services          │
│ (Shared UI Library) │           └──────────────────────┘
└─────────────────────┘                     │
                                           │
                                           ▼
                              ┌────────────────────────┐
                              │  External Dependencies │
                              │  - Azure Blob Storage  │
                              │  - SQLite (EF Core)    │
                              │  - Zio FileSystem      │
                              └────────────────────────┘
```

## Technology Stack

- **Framework**: .NET 10.0
- **Architecture**: CQRS with Mediator pattern, Vertical Slice Architecture
- **Storage**: Azure Blob Storage, SQLite (via EF Core)
- **UI**: WPF with CommunityToolkit.Mvvm
- **CLI**: CliFx + Spectre.Console
- **Testing**: xUnit, NSubstitute, Shouldly
- **Logging**: Serilog

## Key Features

- ✅ File-level deduplication
- ✅ Client-side AES256 encryption
- ✅ Pointer files for filesystem visibility
- ✅ Tiered archival to Azure Blob Storage
- ✅ Domain-Driven Design with value objects
- ✅ Central package management
