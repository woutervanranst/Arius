# Archive Command Documentation

## Overview

The Archive Command handles the process of uploading files to Azure Blob Storage with client-side encryption and compression. It implements a tiered archival solution that optimizes storage costs through deduplication and intelligent storage tier management.

## File Processing Strategy

### When Files Are TARred vs Individual Upload

The system uses file size to determine the optimal upload strategy:

**Individual Upload (Large Files)**:
- Files above a certain size threshold are uploaded individually
- Each file becomes a separate chunk in blob storage
- Suitable for large files where the overhead of TAR packaging isn't beneficial

**TAR Archive Upload (Small Files)**:
- Multiple small files are grouped together into TAR archives
- The TAR archive is compressed (gzipped) before encryption
- Reduces the number of individual blob operations
- More efficient for handling many small files

## Upload Flow

### Large Files (Individual Upload)

1. **Read** binary file from local disk
2. **Compress** data using GZip compression
3. **Encrypt** the compressed data using AES256
4. **Upload** encrypted+compressed data to Azure Blob Storage
5. **Create** pointer file locally referencing the stored chunk

**Data transformation**: `Original File → GZip → AES256 → Blob Storage`

### Small Files (TAR Archive Upload)

1. **Create** TAR archive containing multiple small files
2. **Compress** the entire TAR using GZip
3. **Encrypt** the compressed TAR using AES256
4. **Upload** encrypted TAR to Azure Blob Storage
5. **Create** pointer files for each original file referencing their location within the TAR

**Data transformation**: `Multiple Files → TAR → GZip → AES256 → Blob Storage`

## Key Benefits

**Compression Before Encryption**: Data is always compressed before encryption because compression algorithms work more effectively on unencrypted data. Encryption makes data appear random, which significantly reduces compression ratios.

**Deduplication**: Files are deduplicated at the chunk level using SHA256 hashes, ensuring identical content is stored only once regardless of file names or locations.

**Storage Optimization**: The TAR strategy for small files reduces the number of blob storage operations and improves cost efficiency by grouping related files together.

**Client-Side Security**: All encryption occurs on the client side before data leaves the local system, ensuring data privacy and security.

## Stream Processing Architecture

The upload process uses a sophisticated stream chaining approach to handle compression, encryption, and position tracking:

```
Original Data → GZip Compression → AES256 Encryption → Azure Blob Storage
                       ↓                    ↓                 ↓
                 [GZipStream] → [CryptoStream] → [BlobStream]
                       ↑                              ↓
               Writes flow through here    Position read from here
                       ↓                              ↑
               [----------PositionTrackingStream----------]
                                   ↓
                          Returned to caller
```

### PositionTrackingStream Implementation

The system uses a custom `PositionTrackingStream` wrapper that:
- **Delegates write operations** to the compression/encryption pipeline (GZipStream when compression is enabled)
- **Reads position** from the underlying blob stream to track actual bytes written to Azure
- **Maintains seekability** for position tracking while preserving the encryption pipeline
- **Handles disposal** properly without interfering with the blob stream lifecycle

### Compression Control

Compression is explicitly controlled via a boolean parameter rather than content-type detection:
- **Individual files**: `compress: true` - applies GZip compression before encryption
- **TAR archives**: `compress: false` - skips additional compression since TAR files are already compressed

This architecture allows the caller to:
1. Write data through the complete compression + encryption pipeline
2. Track the actual compressed/encrypted bytes written to blob storage
3. Use this information for storage tier optimization and database recording