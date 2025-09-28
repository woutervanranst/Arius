# Archive Command Documentation

## Overview

The **Archive Command** is responsible for orchestrating the archival of files into Azure Blob Storage with client-side compression, encryption, and deduplication.

It implements a **multi-stage pipeline** with parallel processing, TAR batching for small files, and intelligent storage tier management.
The process ensures that identical file content is uploaded only once, storage operations are minimized, and data is secure before leaving the local system.

### Key Features

* **Deduplication**: SHA256 hashes ensure identical files are stored only once.
* **Optimized Storage**: TAR batching reduces blob transactions for small files.
* **Tiering**: Storage tier policies applied automatically after upload.
* **Client-Side Security**: Compression occurs before AES256 encryption, ensuring efficiency and privacy.
* **Resilient Orchestration**: Linked cancellation, error handling, and per-hash gates prevent deadlocks and duplicate uploads.

## Orchestration Flow

The archive command coordinates four concurrent tasks:

1. **Indexing** files from the file system.
2. **Hashing** files and routing them to either large or small pipelines.
3. **Uploading large files** individually.
4. **Batching small files** into compressed TAR archives.

Mermaid overview of the orchestrator:

```mermaid
flowchart TD
%% ================= Orchestrator =================
ORCH_START[Handle start] --> ORCH_LINKED[Create linked cancellation token]
ORCH_LINKED --> ORCH_TASKS[Create tasks: index, hash, upload large, upload small]
ORCH_TASKS --> ORCH_WAIT[Wait for all tasks]
ORCH_WAIT --> ORCH_CLEAN[Delete pointer entries missing on disk]
ORCH_CLEAN --> ORCH_HASCHANGES{State repo has changes}
ORCH_HASCHANGES -->|Yes| ORCH_VACUUM[Vacuum state]
ORCH_VACUUM --> ORCH_UPLOADSTATE[Upload state file]
ORCH_UPLOADSTATE --> ORCH_DONE1[Report progress 100]
ORCH_HASCHANGES -->|No| ORCH_DELETELOCAL[Delete local state file]
ORCH_DONE1 --> ORCH_END[Handle completed]
ORCH_DELETELOCAL --> ORCH_END
```

After processing, the state repository is updated, cleaned, and uploaded if changes occurred. Otherwise, the local state is discarded.


## Pipeline Design

The archival process is built around **channels** that pass work between stages.

### Channels

* **`indexedFilesChannel`** → Produced by the indexer, consumed by hashers.
* **`hashedLargeFilesChannel`** → Produced by hashers, consumed by large file uploaders.
* **`hashedSmallFilesChannel`** → Produced by hashers, consumed by the small file TAR pipeline.

This design allows high throughput with controlled parallelism.


## Index Task

The **indexer** enumerates files from the file system and pushes `FilePair` objects into the `indexedFilesChannel`.

```mermaid
flowchart TD
subgraph INDEX_TASK [Index task]
  direction TB
  IX_ENUM[Enumerate file entries] --> IX_WRITE[Write FilePair to indexed channel]
  IX_WRITE --> IX_COMPLETE[Complete indexed channel at end]
end
```


## Hash Task

The **hasher** processes `FilePair`s in parallel:

* Skips pointer-only files.
* Computes hashes for binary files.
* Routes small files to the small-file TAR path and large files to the large-file path.

```mermaid
flowchart TD
subgraph HASH_TASK [Hash task]
  direction TB
  HS_READ[Read from indexed channel in parallel] --> HS_PTRONLY{File is pointer only}
  HS_PTRONLY -->|Yes| HS_SKIP[Skip]
  HS_PTRONLY -->|No| HS_HASH[Compute hash]
  HS_HASH --> HS_SIZE{Small file boundary}
  HS_SIZE -->|Small| HS_TO_SMALL[Write to hashed small channel]
  HS_SIZE -->|Large| HS_TO_LARGE[Write to hashed large channel]
  HS_TO_SMALL --> HS_ENDSMALL[Complete hashed small channel at end]
  HS_TO_LARGE --> HS_ENDLARGE[Complete hashed large channel at end]
end
```


## Large File Uploads

Files above the **small file boundary** are uploaded individually:

* Only the **first uploader per unique hash** performs the upload (`InFlightGate` ensures single ownership).
* Deduplication ensures duplicates only wait for completion.

```mermaid
flowchart TD
subgraph LARGE_PATH [Upload large files path]
  direction TB
  LG_READ[Read from hashed large channel in parallel] --> LG_HAVE_BP{BinaryProperties exists}
  LG_HAVE_BP -->|Yes| LG_PTR[Create pointer file and upsert entry]
  LG_HAVE_BP -->|No| LG_ENTER[Gate enter by hash]
  LG_ENTER -->|Owner| LG_UPLOAD[Upload large blob now]
  LG_UPLOAD --> LG_ADDBP[Add BinaryProperties and set tier]
  LG_ADDBP --> LG_COMPLETE[Gate complete for hash]
  LG_COMPLETE --> LG_PTR
  LG_ENTER -->|Non owner| LG_WAIT[Await owner task]
  LG_WAIT --> LG_PTR
  LG_PTR --> LG_DONE[Report progress 100]
end
```

**Transformation**:
`Original File → GZip → AES256 → Blob Storage`


## Small File Uploads (TAR Archives)

Small files are aggregated into **TAR archives** to reduce blob operations:

* Owners enqueue their file into the TAR.
* Non-owners (duplicates) defer pointer creation until the owner completes.
* When TAR size reaches threshold (or at end of input), the archive is flushed and uploaded as a **single blob**.

```mermaid
flowchart TD
subgraph SMALL_PATH [Upload small files tar path]
  direction TB
  SM_READ[Read from hashed small channel single reader] --> SM_HAVE_BP{BinaryProperties exists}
  SM_HAVE_BP -->|Yes| SM_PTR_NOW[Create pointer file and upsert entry]
  SM_HAVE_BP -->|No| SM_ENTER[Gate enter by hash]
  SM_ENTER -->|Owner| SM_ADD_TAR[Add entry to in memory tar]
  SM_ADD_TAR --> SM_FLUSH_DECIDE{Flush tar decision}
  SM_FLUSH_DECIDE -->|Yes| SM_PROCESS[Process tar archive]
  SM_FLUSH_DECIDE -->|No| SM_READ

  %% Process TAR results (owners only)
  SM_PROCESS --> SM_CHILD_BP[Add BinaryProperties for children and parent]
  SM_CHILD_BP --> SM_PTR_OWNER[Write pointer entries for owners]
  SM_PTR_OWNER --> SM_COMPLETE_HASH[Gate complete for each owner hash]
  SM_COMPLETE_HASH --> SM_RESET[Reset tar for next batch]
  SM_RESET --> SM_READ

  %% Non owner duplicates are deferred
  SM_ENTER -->|Non owner| SM_DEFER[Defer pointer via continuation on owner task]
  SM_DEFER --> SM_READ

  %% Finalization after channel drains
  SM_READ --> SM_FINAL_DECIDE{Channel complete and tar has entries}
  SM_FINAL_DECIDE -->|Yes| SM_PROCESS
  SM_FINAL_DECIDE -->|No| SM_AWAIT_DEFER[Await all deferred pointer tasks]
  SM_AWAIT_DEFER --> SM_DONE[Report progress as needed]
end
```

**Transformation**:
`Multiple Files → TAR → GZip → AES256 → Blob Storage`


### TAR Processing Details

When a TAR flush occurs:

* Parent TAR stream is hashed and uploaded.
* `BinaryProperties` are recorded for both child files and parent TAR.
* Pointers are written for all files.
* Deferred duplicates are flushed afterward.

```mermaid
flowchart TD
subgraph TAR_DETAILS [Process tar archive details]
  direction TB
  TAR_HASH[Compute hash for TAR] --> TAR_UPLOAD[Upload TAR]
  TAR_UPLOAD --> TAR_TIER[Set storage tier]
  TAR_TIER --> TAR_PARENT_BP[Add BinaryProperties for TAR]
  TAR_PARENT_BP --> TAR_NOTE[Write Pointers & PointerFileEntries of TAR Entries]
end
```