# Detailed Architecture - Core Projects

```
┌─────────────────────────────────────────────────────────────────────┐
│                         User Interaction                            │
│                    (CLI Terminal or WPF UI)                         │
└──────────────────────────┬──────────────────────────────────────────┘
                           │
              ┌────────────┴───────────┐
              │                        │
              ▼                        ▼
    ┌──────────────────┐    ┌──────────────────┐
    │   Arius.Cli      │    │ Arius.Explorer   │
    │   (CliFx)        │    │   (WPF/MVVM)     │
    └────────┬─────────┘    └────────┬─────────┘
             │                       │
             └───────────┬───────────┘
                         │  IMediator.Send()
                         ▼
              ┌────────────────────┐
              │   Arius.Core       │
              │   (Business Logic) │
              │                    │
              │  - Validators      │
              │  - Handlers        │
              │  - Domain Models   │
              └─────────┬──────────┘
                        │
        ┌───────────────┼───────────────┐
        │               │               │
        ▼               ▼               ▼
┌─────────────┐  ┌─────────────┐  ┌─────────────┐
│   SQLite    │  │  Azure Blob │  │  Local File │
│ (Metadata)  │  │  (Storage)  │  │   System    │
└─────────────┘  └─────────────┘  └─────────────┘
```


## Architecture Principles

### 1. **Vertical Slice Architecture** (Arius.Core)
   - Each feature is self-contained in its own folder
   - Command/Query + Handler + Validator co-located
   - Reduces coupling between features

### 2. **CQRS Pattern**
   - Commands: Write operations (Archive, Restore)
   - Queries: Read operations (PointerFileEntries, ContainerNames)
   - Clear separation of concerns

### 3. **Mediator Pattern**
   - Decouples CLI/WPF from Core handlers
   - Single responsibility for handlers
   - Testable in isolation

### 4. **Domain-Driven Design**
   - Value Objects: `Hash`, `FilePair`
   - Entities with identity
   - Rich domain models

# 1. Arius.Core - Business Logic Layer

```
Arius.Core/
│
├── Features/                           # Vertical Slice Architecture
│   │
│   ├── Commands/                       # Write operations (CQRS)
│   │   ├── Archive/
│   │   │   ├── ArchiveCommand.cs              # Command definition
│   │   │   ├── ArchiveCommandHandler.cs       # Command handler
│   │   │   ├── ArchiveCommandValidator.cs     # FluentValidation
│   │   │   ├── HandlerContext.cs              # Immutable context record
│   │   │   ├── HandlerContextBuilder.cs       # Context initialization
│   │   │   └── ...
│   │   │
│   │   └── Restore/
│   │       ├── RestoreCommand.cs
│   │       ├── RestoreCommandHandler.cs
│   │       ├── RestoreCommandValidator.cs
│   │       ├── HandlerContext.cs              # Immutable context record
│   │       ├── HandlerContextBuilder.cs       # Context initialization
│   │       └── ...
│   │
│   └── Queries/                        # Read operations (CQRS)
│       ├── ContainerNames/
│       │   ├── ContainerNamesQuery.cs
│       │   ├── ContainerNamesQueryHandler.cs
│       │   └── ...
│       │
│       └── PointerFileEntries/
│           ├── PointerFileEntriesQuery.cs
│           ├── PointerFileEntriesQueryHandler.cs
│           ├── HandlerContext.cs              # Immutable context record
│           ├── HandlerContextBuilder.cs       # Context initialization
│           └── ...
│
└── Shared/                             # Shared infrastructure & domain
    ├── Concurrency/                    # Async/parallel processing
    ├── Crypto/                         # AES256 encryption/decryption
    ├── Extensions/                     # Utility extensions
    ├── FileSystem/                     # Zio filesystem abstractions
    │   └── FilePairFileSystem.cs              # Pointer + Binary file handling
    ├── Hashing/                        # SHA256 hashing
    │   └── Hash.cs                            # Immutable value object
    ├── Progress/                       # Progress reporting
    ├── StateRepositories/              # SQLite persistence layer
    │   ├── StateRepository.cs
    │   ├── Migrations/                        # EF Core migrations
    │   └── ...
    └── Storage/                        # Azure Blob Storage
        └── BlobStorage.cs                     # Azure client wrapper
```

## Core Concepts Flow

### HandlerContext Pattern

Each feature uses a **HandlerContext** pattern to encapsulate dependencies and initialization:
- **HandlerContext**: Immutable record containing all validated dependencies
- **HandlerContextBuilder**: Fluent builder that initializes and validates the context
- Separates complex setup logic from business logic
- Enables easy testing by allowing dependency injection

```
┌─────────────┐
│   Command   │  (e.g., ArchiveCommand)
└──────┬──────┘
       │
       ▼
┌──────────────────────┐
│ HandlerContextBuilder│  Build context with dependencies
│  - Validate command  │
│  - Init Azure Blob   │
│  - Setup StateRepo   │
│  - Config FileSystem │
└──────┬───────────────┘
       │
       ▼
┌──────────────────────┐
│   HandlerContext     │  Immutable record with:
│  (Immutable Record)  │  - Command/Query
│                      │  - ArchiveStorage (Azure + Encryption)
│                      │  - StateRepository (SQLite)
│                      │  - FilePairFileSystem (Zio)
│                      │  - Hasher (SHA256)
└──────┬───────────────┘
       │
       ▼
┌─────────────────┐
│  Command Handler│  Business logic using context
└──────┬──────────┘
       │
       ├─────▶ context.FileSystem      ─────▶  Local File Operations
       │                                       (Read/Write pointer + binary files)
       │
       ├─────▶ context.StateRepository ─────▶  SQLite
       │                                       (Track uploaded files, metadata)
       │
       ├─────▶ context.ArchiveStorage  ─────▶  Azure Blob Storage
       │                                       (Upload/download encrypted blobs)
       │
       └─────▶ context.Hasher          ─────▶  SHA256 Hash calculation
```

# 2. Arius.Cli - Command Line Interface

```
Arius.Cli/
│
├── Program.cs                          # Entry point, DI setup
│
├── CliCommands/                        # CliFx command definitions
│   ├── ArchiveCommand.cs                      # archive command
│   ├── RestoreCommand.cs                      # restore command
│   └── ...
│       │
│       └──────────────────┐
│                          │  Uses IMediator to dispatch
│                          ▼  to Core handlers
│                    ┌──────────────┐
│                    │ Arius.Core   │
│                    │   Handlers   │
│                    └──────────────┘
│
└── Properties/
    └── launchSettings.json                    # Debug/launch profiles
```

## CLI Command Flow

```
User Input (Terminal)
       │
       ▼
┌─────────────────┐
│  CliFx Command  │  (e.g., ArchiveCommand)
└────────┬────────┘
         │  Parse arguments
         │  Validate input
         ▼
┌─────────────────┐
│   IMediator     │  Dispatch command to Core
└────────┬────────┘
         │
         ▼
┌─────────────────────────┐
│ Core Command Handler    │  Execute business logic
│ (in Arius.Core)         │
└────────┬────────────────┘
         │
         ▼
┌─────────────────────────┐
│  Spectre.Console        │  Display output/progress
│  (Terminal UI)          │
└─────────────────────────┘
```

# 3. Arius.Explorer - WPF Desktop Application

```
Arius.Explorer/
│
├── App.xaml / App.xaml.cs             # Application entry point
├── MainWindow.xaml / MainWindow.xaml.cs
│
├── ChooseRepository/                   # Repository selection feature
│   ├── ChooseRepositoryView.xaml              # XAML view
│   ├── ChooseRepositoryViewModel.cs           # ViewModel (MVVM)
│   └── ...
│
├── RepositoryExplorer/                 # Main explorer feature
│   ├── RepositoryExplorerView.xaml            # XAML view
│   ├── RepositoryExplorerViewModel.cs         # ViewModel (MVVM)
│   └── ...
│
├── Settings/                           # Settings management
│   └── ...
│
├── Shared/                             # Shared WPF components
│   ├── Converters/                            # Value converters
│   ├── Extensions/                            # WPF extensions
│   └── Services/                              # Application services
│
├── Properties/                         # Assembly info, publish profiles
│   └── PublishProfiles/
│
└── Resources/                          # Icons, images, etc.
```

## WPF MVVM Flow

```
┌──────────────────┐
│   View (XAML)    │  MainWindow, ChooseRepositoryView, etc.
└────────┬─────────┘
         │  Data Binding
         │  Commands
         ▼
┌──────────────────────┐
│   ViewModel          │  CommunityToolkit.Mvvm
│  - ObservableObject  │  - Property change notifications
│  - RelayCommand      │  - Command implementations
└────────┬─────────────┘
         │  Uses IMediator
         ▼
┌──────────────────────────┐
│  Core Handlers           │  Arius.Core
│  - ArchiveCommandHandler │
│  - PointerFileEntries    │
│  - QueryHandler          │
└────────┬─────────────────┘
         │
         ▼
┌────────────────────────────┐
│  Infrastructure            │
│  - Azure Blob Storage      │
│  - SQLite StateRepository  │
│  - File System Operations  │
└────────────────────────────┘
```

## Key WPF Patterns

1. **MVVM Pattern**: Views bind to ViewModels using CommunityToolkit.Mvvm
2. **Command Pattern**: RelayCommand/AsyncRelayCommand for user interactions
3. **Mediator Pattern**: ViewModels dispatch to Core via IMediator
4. **Value Converters**: Transform data between View and ViewModel
5. **Dependency Injection**: Services registered in App.xaml.cs