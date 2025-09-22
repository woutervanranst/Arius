using Arius.Core.DbMigrationV3V5.v3;
using Arius.Core.Shared.Crypto;
using Arius.Core.Shared.StateRepositories;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using System.IO.Compression;
using Arius.Core.Shared.Storage;
using Microsoft.EntityFrameworkCore;

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
            var accountName       = repositoryOptions["AccountName"] ?? throw new InvalidOperationException("AccountName is required");
            var accountKey        = repositoryOptions["AccountKey"] ?? throw new InvalidOperationException("AccountKey is required");
            //var containerName = repositoryOptions["ContainerName"] ?? throw new InvalidOperationException("ContainerName is required");
            var passphrase = repositoryOptions["Passphrase"] ?? throw new InvalidOperationException("Passphrase is required");

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

            if (v3BlobClient.GetProperties().Value.Metadata.TryGetValue("DatabaseVersion", out var version) && version == "5")
                return;

            var v3LocalDbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Arius", "states", containerName, "v3.sqlite");
            var v5LocalDbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Arius", "states", containerName, "v5.sqlite");

            if (!File.Exists(v3LocalDbPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(v3LocalDbPath));

                await using var ssv2 = await v3BlobClient.OpenReadAsync();
                await using var tsv2 = new FileStream(v3LocalDbPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None, bufferSize: 4096); //File.OpenWrite(localDbPath); // do not use asyncIO for small files

                await using var decryptedStream    = await ssv2.GetDecryptionStreamAsync(passphrase);
                await using var decompressedStream = new GZipStream(decryptedStream, CompressionMode.Decompress);

                await decompressedStream.CopyToAsync(tsv2);
            }

            await using var v3db = new StateDbContextV3(v3LocalDbPath);
            await using (var v5db = new StateRepositoryDbContext(new DbContextOptionsBuilder<StateRepositoryDbContext>()
                             .UseSqlite($"Data Source={v5LocalDbPath}")
                             .Options))
            {
                await v5db.Database.EnsureCreatedAsync();

                // Migrate ChunkEntries to BinaryProperties
                if (v3db.ChunkEntries.Count() != v5db.BinaryProperties.Count())
                {
                    v5db.BinaryProperties.RemoveRange(v5db.BinaryProperties);
                    v5db.SaveChanges();

                    foreach (var ce in v3db.ChunkEntries)
                    {
                        if (ce.ChunkCount > 1)
                            throw new InvalidOperationException("Cannot migrate ChunkEntry with ChunkCount > 1 to BinaryProperties");
                        if (ce.IncrementalLength != ce.ArchivedLength)
                            throw new InvalidOperationException("Cannot migrate ChunkEntry with IncrementalLength != ArchivedLength to BinaryProperties");


                        var bp = new BinaryProperties()
                        {
                            Hash         = ce.Hash,
                            ParentHash   = null,
                            OriginalSize = ce.OriginalLength,
                            ArchivedSize = ce.ArchivedLength,
                            StorageTier  = ToStorageTier(ce.AccessTier)
                        };

                        v5db.BinaryProperties.Add(bp);
                    }

                    v5db.SaveChanges();
                }

                //    if (v3db.BinaryProperties.Count() != v5db.ChunkEntries.Count())
                //    {
                //        v5db.ChunkEntries.RemoveRange(v5db.ChunkEntries);
                //        await v5db.SaveChangesAsync();

                //        var chunks = await container.GetBlobsAsync(prefix: $"{BlobContainer.CHUNKS_FOLDER_NAME}/").ToDictionaryAsync(bi => Path.GetFileName(bi.Name), bi => bi);

                //        foreach (var bp in v3db.BinaryProperties)
                //        {
                //            var ce = new ChunkEntry
                //            {
                //                Hash = bp.Hash.HexStringToBytes(),
                //                OriginalLength = bp.OriginalLength,
                //                ArchivedLength = bp.ArchivedLength,
                //                IncrementalLength = bp.IncrementalLength,
                //                ChunkCount = bp.ChunkCount,
                //                AccessTier = chunks[bp.Hash].Properties.AccessTier
                //            };

                //            if (ce.AccessTier == AccessTier.Cool)
                //            {
                //                // Also update the Tier
                //                var blobClient = container.GetBlobClient($"{BlobContainer.CHUNKS_FOLDER_NAME}/{bp.Hash}");
                //                blobClient.SetAccessTierAsync(AccessTier.Cold);
                //            }

                //            v5db.ChunkEntries.Add(ce);
                //        }

                //        await v5db.SaveChangesAsync();
                //    }

                //    // Migrate PointerFileEnties
                //    if (v3db.PointerFileEntries.Count() != v5db.PointerFileEntries.Count())
                //    {
                //        v5db.PointerFileEntries.RemoveRange(v5db.PointerFileEntries);
                //        await v5db.SaveChangesAsync();

                //        foreach (var v2pfe in v3db.PointerFileEntries)
                //        {
                //            var v3pfe = new PointerFileEntry
                //            {
                //                BinaryHashValue = v2pfe.BinaryHash.HexStringToBytes(),
                //                RelativeName = v2pfe.RelativeName,
                //                VersionUtc = v2pfe.VersionUtc,
                //                IsDeleted = v2pfe.IsDeleted,
                //                CreationTimeUtc = v2pfe.CreationTimeUtc,
                //                LastWriteTimeUtc = v2pfe.LastWriteTimeUtc
                //            };

                //            v5db.PointerFileEntries.Add(v3pfe);
                //        }

                //        await v5db.SaveChangesAsync();
                //    }

                //    //await v3db.DisposeAsync();

                //    await v5db.Database.ExecuteSqlRawAsync("VACUUM;");
                //    //await v3db.Database.CloseConnectionAsync(); 
            }

            SqliteConnection.ClearAllPools(); // https://github.com/dotnet/efcore/issues/26580#issuecomment-1042924993


            //// Upload
            //var v5BlobClient = container.GetBlobClient($"{BlobContainer.STATE_DBS_FOLDER_NAME}/{DateTime.UtcNow:s}");
            //await using var ssv3 = File.OpenRead(v5LocalDbPath);
            //await using var tsv3 = await v5BlobClient.OpenWriteAsync(overwrite: true);
            //await CryptoService.CompressAndEncryptAsync(ssv3, tsv3, passphrase);

            //await v5BlobClient.SetAccessTierAsync(AccessTier.Cold);
            //await v5BlobClient.SetHttpHeadersAsync(new BlobHttpHeaders { ContentType = CryptoService.ContentType });
            //await v5BlobClient.SetMetadataAsync(new Dictionary<string, string> { { "MigrationResult", lastStateBlobName }, { "DatabaseVersion", "3" } });
        }

        private static StorageTier ToStorageTier(AccessTier? tier)
        {
            if (tier == null)
                throw new ArgumentOutOfRangeException();

            if (tier == AccessTier.Hot)
                return StorageTier.Hot;

            if (tier == AccessTier.Cool)
                return StorageTier.Cool;

            if (tier == AccessTier.Cold)
                return StorageTier.Cold;

            if (tier == AccessTier.Archive)
                return StorageTier.Archive;

            else throw new ArgumentOutOfRangeException();
        }
    }
}