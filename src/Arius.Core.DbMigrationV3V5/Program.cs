using Arius.Core.DbMigrationV3V5.v3;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace Arius.Core.DbMigrationV3V5
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            var configuration = new ConfigurationBuilder()
                //.SetBasePath(Directory.GetCurrentDirectory())
                //.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddUserSecrets<Program>(optional: true)
                .Build();

            var repositoryOptions = configuration.GetSection("RepositoryOptions");
            var accountName   = repositoryOptions["AccountName"] ?? throw new InvalidOperationException("AccountName is required");
            var accountKey    = repositoryOptions["AccountKey"] ?? throw new InvalidOperationException("AccountKey is required");
            //var containerName = repositoryOptions["ContainerName"] ?? throw new InvalidOperationException("ContainerName is required");
            var passphrase    = repositoryOptions["Passphrase"] ?? throw new InvalidOperationException("Passphrase is required");

            var bsc = new BlobServiceClient(new Uri($"https://{accountName}.blob.core.windows.net/"), new StorageSharedKeyCredential(accountName, accountKey));

            foreach (var blobContainerItem in bsc.GetBlobContainers())
                await MigrateContainerAsync(bsc, blobContainerItem.Name, passphrase);
        }

        private static async Task MigrateContainerAsync(BlobServiceClient bsc, string containerName, string passphrase)
        {
            var container = bsc.GetBlobContainerClient(containerName);

            var lastStateBlobName = await container.GetBlobsAsync(prefix: "states/")
                .Select(be => be.Name)
                .OrderBy(b => b)
                .LastOrDefaultAsync();

            var v3BlobClient = container.GetBlobClient(lastStateBlobName);

            var v3LocalDbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Arius", "states", containerName, "v2.sqlite");
            var v5LocalDbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Arius", "states", containerName, "v3.sqlite");

            if (!File.Exists(v3LocalDbPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(v3LocalDbPath));

                await using var ssv2 = await v3BlobClient.OpenReadAsync();
                await using var tsv2 = new FileStream(v3LocalDbPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None, bufferSize: 4096); //File.OpenWrite(localDbPath); // do not use asyncIO for small files
                //await CryptoService.DecryptAndDecompressAsync(ssv2, tsv2, passphrase);
            }

            await using var v3db = new StateDbContextV2(v3LocalDbPath);
            await using (var v5db = new StateDbContext(v5LocalDbPath))
            {
                await v5db.Database.EnsureCreatedAsync();

                // Migrate BinaryProperties to ChunkEntries
                if (v3db.BinaryProperties.Count() != v5db.ChunkEntries.Count())
                {
                    v5db.ChunkEntries.RemoveRange(v5db.ChunkEntries);
                    await v5db.SaveChangesAsync();

                    var chunks = await container.GetBlobsAsync(prefix: $"{BlobContainer.CHUNKS_FOLDER_NAME}/").ToDictionaryAsync(bi => Path.GetFileName(bi.Name), bi => bi);

                    foreach (var bp in v3db.BinaryProperties)
                    {
                        var ce = new ChunkEntry
                        {
                            Hash = bp.Hash.HexStringToBytes(),
                            OriginalLength = bp.OriginalLength,
                            ArchivedLength = bp.ArchivedLength,
                            IncrementalLength = bp.IncrementalLength,
                            ChunkCount = bp.ChunkCount,
                            AccessTier = chunks[bp.Hash].Properties.AccessTier
                        };

                        if (ce.AccessTier == AccessTier.Cool)
                        {
                            // Also update the Tier
                            var blobClient = container.GetBlobClient($"{BlobContainer.CHUNKS_FOLDER_NAME}/{bp.Hash}");
                            blobClient.SetAccessTierAsync(AccessTier.Cold);
                        }

                        v5db.ChunkEntries.Add(ce);
                    }

                    await v5db.SaveChangesAsync();
                }

                // Migrate PointerFileEnties
                if (v3db.PointerFileEntries.Count() != v5db.PointerFileEntries.Count())
                {
                    v5db.PointerFileEntries.RemoveRange(v5db.PointerFileEntries);
                    await v5db.SaveChangesAsync();

                    foreach (var v2pfe in v3db.PointerFileEntries)
                    {
                        var v3pfe = new PointerFileEntry
                        {
                            BinaryHashValue = v2pfe.BinaryHash.HexStringToBytes(),
                            RelativeName = v2pfe.RelativeName,
                            VersionUtc = v2pfe.VersionUtc,
                            IsDeleted = v2pfe.IsDeleted,
                            CreationTimeUtc = v2pfe.CreationTimeUtc,
                            LastWriteTimeUtc = v2pfe.LastWriteTimeUtc
                        };

                        v5db.PointerFileEntries.Add(v3pfe);
                    }

                    await v5db.SaveChangesAsync();
                }

                //await v3db.DisposeAsync();

                await v5db.Database.ExecuteSqlRawAsync("VACUUM;");
                //await v3db.Database.CloseConnectionAsync(); 
            }

            SqliteConnection.ClearAllPools(); // https://github.com/dotnet/efcore/issues/26580#issuecomment-1042924993


            // Upload
            var v3BlobClient = container.GetBlobClient($"{BlobContainer.STATE_DBS_FOLDER_NAME}/{DateTime.UtcNow:s}");
            await using var ssv3 = File.OpenRead(v5LocalDbPath);
            await using var tsv3 = await v3BlobClient.OpenWriteAsync(overwrite: true);
            await CryptoService.CompressAndEncryptAsync(ssv3, tsv3, passphrase);

            await v3BlobClient.SetAccessTierAsync(AccessTier.Cold);
            await v3BlobClient.SetHttpHeadersAsync(new BlobHttpHeaders { ContentType = CryptoService.ContentType });
            await v3BlobClient.SetMetadataAsync(new Dictionary<string, string> { { "MigrationResult", lastStateBlobName }, { "DatabaseVersion", "3" } });
        }
    }

}
