### Deduplication

#### How it works

1. Scan the file system for files meeting the optimization policy.

![](https://docs.microsoft.com/en-us/windows-server/storage/data-deduplication/media/understanding-dedup-how-dedup-works-1.gif)

2. Break files into variable-size chunks.

![alt](https://docs.microsoft.com/en-us/windows-server/storage/data-deduplication/media/understanding-dedup-how-dedup-works-2.gif)

3. Identify unique chunks.

![alt](https://docs.microsoft.com/en-us/windows-server/storage/data-deduplication/media/understanding-dedup-how-dedup-works-3.gif)

4. Place chunks in the chunk store and optionally compress.

![alt](https://docs.microsoft.com/en-us/windows-server/storage/data-deduplication/media/understanding-dedup-how-dedup-works-4.gif)

#### In detail

![](docs/overview2.svg)

1. The filesystem is indexed. Arius finds `report v1.doc` and calculates the SHA256 hash to be `binaryhash1`. This binaryhash does not yet exist in blob storage, so this binary needs to be uploaded.
2. Arius breaks ("chunks") the file into variable sized chunks: `chunkhash1`, `chunkhash2` and `chunkhash3`. These chunkhashes do not yet exist in blob storage, so these chunks need to be uploaded.
3. `chunkhash1`, `chunkhash2` and `chunkhash3` are compressed (using gzip) and encrypted (using AES256) and then uploaded to the `chunks` container in blob storage. This is the bulk of the storage size and the use of the archive tier is highly advised, as the chunks are not needed until restore.
4. A `manifest` is created describing the chunks that make up the binary.
5. A `metadata entry` is created describing the original length, archived length and the number of chunks. While not strictly required (easier to consult size etc)
6. A pointer is created on the local file system (`report v1.doc.pointer.arius`), containing just the 64 hex characters of the SHA256 hash of the original binary (`binaryhash1`). Optionally, the original binary can be deleted since it is not succesfully archived.
7. A `pointerfile entry` is created linking the `binaryhash` with the path on the local filesystem and the point-in-time version. This is used when restoring the full archive onto an empty local disk.<br><br>
8. The filesystem is further indexed. Arius finds `report v1 (copy).doc` and calculates the SHA256 hash to be `binaryhash1`. This binaryhash *already* exists in blob storage so this binary does *not* need to be uploaded.
9. A pointer is created on the local file system (`report v1 (copy).doc.pointer.arius`), containing just the 64 hex characters of the SHA256 hash of the original binary (`binaryhash1`). Optionally, the original binary can be deleted since it is not succesfully archived.
10.  A `pointerfile entry` is created linking the `binaryhash` with the path on the local filesystem and the point-in-time version. This is used when restoring the full archive onto an empty local disk.<br><br>
11. The filesystem is further indexed. Arius finds `report v2.doc` and calculates the SHA256 hash to be `binaryhash2`. This binaryhash does not yet exist in blob storage, so this binary needs to be uploaded.
12. Arius breaks ("chunks") the file into variable sized chunks: `chunkhash1`, `chunkhash2` and `chunkhash4`. Only the last chunk does not exist in blob storage, so only that one needs to be uploaded.
13. `chunkhash4` is compressed, encrypted and uploaded to the `chunks` container in blob storage.
14. A `manifest` is created describing the chunks that make up the binary.
15. A pointer is created on the local file system (`report v2.doc.pointer.arius`), containing just the 64 hex characters of the SHA256 hash of the original binary (`binaryhash2`). Optionally, the original binary can be deleted since it is not succesfully archived.
16. 


A 1 GB file chunked into chunks of 64 KB, with each chunk having a SHA256 hash (32 bytes = 64 hex characters) * 4 bytes/UTF8 character = 4 MB of manifest

((1 GB) / (64 KB)) * (64 * 4 bytes) = 4 megabytes

#### Deduplication benchmark for large binary files 

12 mp4 files of on average 192 MB, totalling 2,24 GB. Benchmark performed on an Azure D8s_v3 VM in the same region as the storage account.

| Min. Chunk Size (B) | Original KB | Total Chunks | Deduped KB | Archive KB | Time  | MBps  | Avg Chunk Size (KB) | Compression | Compression + Dedup |
|-|-|-|-|-|-|-|-|-|-|
| N/A  | 2.410.419.052  |      12  |      0  | 2.333.672 |  0:45 | 51,08 |   N/A | 99,14% | 99,14% |
| 1024 | 2.410.419.052  | 262.666  | 125,19  | 2.346.100 | 36:00 | 1,06  |  8,93 | 99,67% | 99,67% |
| 4096 | 2.410.419.052  | 174.589  | 112,33  | 2.341.994 | 24:00 | 1,60  | 13,41 | 99,50% | 99,49% |
| 8192 | 2.410.419.052  | 165.619  | 111,63  | 2.341.567 | 23:00 | 1,67  | 14,14 | 99,48% | 99,48% |


Conclusions:
- While the chunking algorithm finds duplicate chunks in mp4 files, it is not more effective in achieving a better end result than plain gzip compression (99,48% vs 99,14%)
- A minimum chunk size of 4096 KB achieves the best characteristics while keeping the flexibility of chunking:
  - A significant reduction in number of chunks and runtime (-33%) 
  - A significant increase in speed (+50%)
  - A comparable number of usable deduped chunks (only 11% less)
- A minimum chunk size of 8192 KB achieves further improvements across these dimensions but only marginally so, while significantly reducing the potential on bigger datasets
- These conclusions have been confirmed on a larger dataset (20,7 GB of mp4 files).

#### Deduplication benchmark on general purpose file share

31.757 files totalling 71,7 GB of general purpose files (historical backups).

| Min. Chunk Size (B) | Original GB | Total Chunks | Deduped GB | Archive GB | Time  | MBps  | Avg Chunk Size (KB) | Compression | Compression + Dedup |
|-|-|-|-|-|-|-|-|-|-|
| 4096  | 71,704  | 2469531 |      3,219  | 65,943 | - | - |   29,37 | 96,45% | 91,97% |

Conclusions:
- General purpose files are much better suited for deduplication and offer a significant increase in space reduction compared to gzip