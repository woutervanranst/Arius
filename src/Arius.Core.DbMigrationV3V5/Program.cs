using Arius.Core.DbMigrationV3V5.v3;
using Arius.Core.Shared.Crypto;
using Arius.Core.Shared.StateRepositories;
using Arius.Core.Shared.Storage;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System.IO.Compression;
using PointerFileEntryV5 = Arius.Core.Shared.StateRepositories.PointerFileEntry;

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

                        //var chunks = await container.GetBlobsAsync(prefix: $"{BlobContainer.CHUNKS_FOLDER_NAME}/").ToDictionaryAsync(bi => Path.GetFileName(bi.Name), bi => bi);

                        var bp = new BinaryProperties
                        {
                            Hash         = ce.Hash,
                            ParentHash   = null,
                            OriginalSize = ce.OriginalLength, ArchivedSize = ce.ArchivedLength,
                            StorageTier  = ToStorageTier(ce.AccessTier)
                        };

                        v5db.BinaryProperties.Add(bp);
                    }

                    v5db.SaveChanges();
                }


                // Migrate PointerFileEntries
                var v3PointerFileEntries = v3db.PointerFileEntries
                    .Where(pfe => pfe.VersionUtc <= DateTime.UtcNow)
                    .GroupBy(pfe => pfe.RelativeName)
                    .Select(g => g.OrderByDescending(pfe => pfe.VersionUtc).FirstOrDefault()).ToArray();

                var v3ExistingPointerFileEntries = v3PointerFileEntries.Where(pfe => !pfe.IsDeleted).ToArray();
                
                if (v3ExistingPointerFileEntries.Count() != v5db.PointerFileEntries.Count())
                {
                    v5db.PointerFileEntries.RemoveRange(v5db.PointerFileEntries);
                    v5db.SaveChanges();

                    foreach (var v3pfe in v3ExistingPointerFileEntries)
                    {
                        if (v3pfe.IsDeleted)
                            continue;

                        var v5pfe = new PointerFileEntryV5
                        {
                            Hash             = v3pfe.BinaryHashValue,
                            RelativeName     = v3pfe.RelativeName.ToPlatformNeutralPath(),
                            CreationTimeUtc  = v3pfe.CreationTimeUtc,
                            LastWriteTimeUtc = v3pfe.LastWriteTimeUtc
                        };

                        v5db.PointerFileEntries.Add(v5pfe);
                    }

                    v5db.SaveChanges();
                }


                v5db.Database.ExecuteSqlRaw("VACUUM;");
            }

            SqliteConnection.ClearAllPools(); // https://github.com/dotnet/efcore/issues/26580#issuecomment-1042924993

            // Upload
            var v5BlobClient = container.GetBlobClient($"states/{DateTime.UtcNow:s}");
            await using (var ssv5 = File.OpenRead(v5LocalDbPath))
            {
                await using var tsv5 = await v5BlobClient.OpenWriteAsync(overwrite: true);

                await using var encryptedStream  = await tsv5.GetEncryptionStreamAsync(passphrase);
                await using var compressedStream = new GZipStream(encryptedStream, CompressionLevel.SmallestSize);

                await ssv5.CopyToAsync(compressedStream);
            }

            await v5BlobClient.SetAccessTierAsync(AccessTier.Cold);
            await v5BlobClient.SetHttpHeadersAsync(new BlobHttpHeaders { ContentType = "application/aes256cbc+gzip" });
            await v5BlobClient.SetMetadataAsync(new Dictionary<string, string> { { "MigrationResult", lastStateBlobName }, { "DatabaseVersion", "5" } });
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