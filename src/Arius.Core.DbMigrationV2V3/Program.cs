using Arius.Core.Extensions;
using Arius.Core.Repositories.BlobRepository;
using Arius.Core.Repositories.StateDb;
using Arius.Core.Services;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WouterVanRanst.Utils.Extensions;

namespace Arius.Core.DbMigrationV2V3
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            var accountName   = "";
            var accountKey    = "";
            var containerName = "";
            var passphrase    = "";

            var bsc       = new BlobServiceClient(new Uri($"https://{accountName}.blob.core.windows.net/"), new StorageSharedKeyCredential(accountName, accountKey));

            foreach (var blobContainerItem in bsc.GetBlobContainers())
                await MirateContainerAsync(bsc, blobContainerItem.Name, passphrase);
        }

        private static async Task MirateContainerAsync(BlobServiceClient bsc, string containerName, string passphrase)
        {
            var container = bsc.GetBlobContainerClient(containerName);

            var lastStateBlobName = await container.GetBlobsAsync(prefix: $"{BlobContainer.STATE_DBS_FOLDER_NAME}")
                .Select(be => be.Name)
                .OrderBy(b => b)
                .LastOrDefaultAsync();

            var v2BlobClient = container.GetBlobClient(lastStateBlobName);

            if (v2BlobClient.GetProperties().Value.Metadata.ContainsKey("DatabaseVersion"))
                return;

            var v2LocalDbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Arius", "states", containerName, "v2.sqlite");
            var v3LocalDbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Arius", "states", containerName, "v3.sqlite");

            if (!File.Exists(v2LocalDbPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(v2LocalDbPath));

                await using var ssv2 = await v2BlobClient.OpenReadAsync();
                await using var tsv2 = new FileStream(v2LocalDbPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None, bufferSize: 4096); //File.OpenWrite(localDbPath); // do not use asyncIO for small files
                await CryptoService.DecryptAndDecompressAsync(ssv2, tsv2, passphrase);
            }

            await using var v2db = new StateDbContextV2(v2LocalDbPath);
            await using (var v3db = new StateDbContext(v3LocalDbPath))
            {
                await v3db.Database.EnsureCreatedAsync();

                // Migrate BinaryProperties to ChunkEntries
                if (v2db.BinaryProperties.Count() != v3db.ChunkEntries.Count())
                {
                    v3db.ChunkEntries.RemoveRange(v3db.ChunkEntries);
                    await v3db.SaveChangesAsync();

                    var chunks = await container.GetBlobsAsync(prefix: $"{BlobContainer.CHUNKS_FOLDER_NAME}/").ToDictionaryAsync(bi => Path.GetFileName(bi.Name), bi => bi);

                    foreach (var bp in v2db.BinaryProperties)
                    {
                        var ce = new ChunkEntry
                        {
                            Hash              = bp.Hash.HexStringToBytes(),
                            OriginalLength    = bp.OriginalLength,
                            ArchivedLength    = bp.ArchivedLength,
                            IncrementalLength = bp.IncrementalLength,
                            ChunkCount        = bp.ChunkCount,
                            AccessTier        = chunks[bp.Hash].Properties.AccessTier
                        };

                        if (ce.AccessTier == AccessTier.Cool)
                        {
                            // Also update the Tier
                            var blobClient = container.GetBlobClient($"{BlobContainer.CHUNKS_FOLDER_NAME}/{bp.Hash}");
                            blobClient.SetAccessTierAsync(AccessTier.Cold);
                        }

                        v3db.ChunkEntries.Add(ce);
                    }

                    await v3db.SaveChangesAsync();
                }

                // Migrate PointerFileEnties
                if (v2db.PointerFileEntries.Count() != v3db.PointerFileEntries.Count())
                {
                    v3db.PointerFileEntries.RemoveRange(v3db.PointerFileEntries);
                    await v3db.SaveChangesAsync();

                    foreach (var v2pfe in v2db.PointerFileEntries)
                    {
                        var relativePath       = Path.GetDirectoryName(v2pfe.RelativeName);
                        var lastSepIndex       = relativePath.LastIndexOf(Path.DirectorySeparatorChar);
                        var directoryName      = relativePath[(lastSepIndex + 1)..];
                        var relativeParentPath = lastSepIndex == -1 ? "" : relativePath[..lastSepIndex];

                        var v3pfe = new PointerFileEntryDto
                        {
                            BinaryHash         = v2pfe.BinaryHash.HexStringToBytes(),
                            RelativeParentPath = PointerFileEntryConverter.ToPlatformNeutralPath(relativeParentPath),
                            DirectoryName      = directoryName,
                            Name               = Path.GetFileName(v2pfe.RelativeName),
                            VersionUtc         = v2pfe.VersionUtc,
                            IsDeleted          = v2pfe.IsDeleted,
                            CreationTimeUtc    = v2pfe.CreationTimeUtc,
                            LastWriteTimeUtc   = v2pfe.LastWriteTimeUtc
                        };

                        v3db.PointerFileEntries.Add(v3pfe);
                    }

                    await v3db.SaveChangesAsync();
                }

                //await v3db.DisposeAsync();

                await v3db.Database.ExecuteSqlRawAsync("VACUUM;");
                //await v3db.Database.CloseConnectionAsync(); 
            }

            SqliteConnection.ClearAllPools(); // https://github.com/dotnet/efcore/issues/26580#issuecomment-1042924993


            // Upload
            var             v3BlobClient = container.GetBlobClient($"{BlobContainer.STATE_DBS_FOLDER_NAME}/{DateTime.UtcNow:s}");
            await using var ssv3         = File.OpenRead(v3LocalDbPath);
            await using var tsv3         = await v3BlobClient.OpenWriteAsync(overwrite: true);
            await CryptoService.CompressAndEncryptAsync(ssv3, tsv3, passphrase);

            await v3BlobClient.SetAccessTierAsync(AccessTier.Cold);
            await v3BlobClient.SetHttpHeadersAsync(new BlobHttpHeaders() { ContentType = CryptoService.ContentType });
            await v3BlobClient.SetMetadataAsync(new Dictionary<string, string> { { "MigrationResult", lastStateBlobName }, { "DatabaseVersion", "3" } });
        }
    }
}