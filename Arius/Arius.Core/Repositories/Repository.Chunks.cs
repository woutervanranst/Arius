using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Arius.Core.Models;
using Arius.Core.Services;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Microsoft.Extensions.Logging;
using static Arius.Core.Facade.Facade;

namespace Arius.Core.Repositories
{
    internal partial class Repository
    {
        internal const string ChunkDirectoryName = "chunks";
        internal const string RehydratedChunkDirectoryName = "chunks-rehydrated";

        private void InitChunkRepository(IOptions options, out string passphrase)
        {
            // 'Partial constructor' for this part of the repo
            passphrase = options.Passphrase;
        }

        private readonly string passphrase;

        // GET

        public ChunkBlobBase[] GetAllChunkBlobs()
        {
            logger.LogInformation($"Getting all ChunkBlobs...");
            var r = Array.Empty<ChunkBlobBase>();

            try
            {
                return r = container.GetBlobs(prefix: $"{ChunkDirectoryName}/")
                    .Select(bi => new ChunkBlobItem(bi))
                    .ToArray();
            }
            finally
            {
                logger.LogInformation($"Getting all ChunkBlobs... got {r.Length}");
            }
        }


        /// <summary>
        /// Get the RemoteEncryptedChunkBlobItem - either from permanent cold storage or from temporary rehydration storage
        /// If the chunk does not exist, throws an InvalidOperationException
        /// If requireHydrated is true and the chunk does not exist in cold storage, returns null
        /// </summary>
        public ChunkBlobBase GetChunkBlobByHash(ChunkHash chunkHash, bool requireHydrated)
        {
            var blobName = GetChunkBlobName(ChunkDirectoryName, chunkHash);
            var cb1 = GetChunkBlobByName(blobName);

            if (cb1 is null)
                throw new InvalidOperationException($"No Chunk for hash {chunkHash.Value}");

            // if we don't need a hydrated chunk, return this one
            if (!requireHydrated)
                return cb1;

            // if we require a hydrated chunk, and this one is hydrated, return this one
            if (requireHydrated && cb1.Downloadable)
                return cb1;

            blobName = GetChunkBlobName(RehydratedChunkDirectoryName, chunkHash);
            var cb2 = GetChunkBlobByName(blobName);

            if (cb2 is null || !cb2.Downloadable)
            {
                // no hydrated chunk exists
                logger.LogDebug($"No hydrated chunk found for {chunkHash}");
                return null;
            }
            else
                return cb2;
        }

        private string GetChunkBlobName(string folder, ChunkHash chunkHash) => GetChunkBlobFullName(folder, $"{chunkHash.Value}{ChunkBlobBase.Extension}");
        private string GetChunkBlobFullName(string folder, string name) => $"{folder}/{name}";

        /// <summary>
        /// Get a ChunkBlobBase in the given folder with the given name.
        /// Return null if it doesn't exist.
        /// </summary>
        /// <param name="folder"></param>
        /// <param name="name"></param>
        /// <returns></returns>
        internal ChunkBlobBase GetChunkBlobByName(string folder, string name) => GetChunkBlobByName(GetChunkBlobFullName(folder, name));

        /// <summary>
        /// Get a ChunkBlobBase by FullName.
        /// Return null if it doesn't exist.
        /// </summary>
        /// <returns></returns>
        internal ChunkBlobBase GetChunkBlobByName(string blobName)
        {
            try
            {
                var bc = container.GetBlobClient(blobName);
                var cb = ChunkBlobBase.GetChunkBlob(bc);
                return cb;
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        public async Task<bool> ChunkExists(ChunkHash chunkHash)
        {
            return await container.GetBlobClient(GetChunkBlobName(ChunkDirectoryName, chunkHash)).ExistsAsync();
        }


        // HYDRATE

        public void HydrateChunk(ChunkBlobBase blobToHydrate)
        {
            logger.LogInformation($"Hydrating chunk {blobToHydrate.Name}");

            if (blobToHydrate.AccessTier == AccessTier.Hot ||
                blobToHydrate.AccessTier == AccessTier.Cool)
                throw new InvalidOperationException($"Calling Hydrate on a blob that is already hydrated ({blobToHydrate.Name})");

            var hydratedItem = container.GetBlobClient($"{RehydratedChunkDirectoryName}/{blobToHydrate.Name}");

            if (!hydratedItem.Exists())
            {
                //Start hydration
                var archiveItem = container.GetBlobClient(blobToHydrate.FullName);
                hydratedItem.StartCopyFromUri(archiveItem.Uri, new BlobCopyFromUriOptions { AccessTier = AccessTier.Cool, RehydratePriority = RehydratePriority.Standard });

                logger.LogInformation($"Hydration started for {blobToHydrate.Name}");
            }
            else
            {
                // Get hydration status
                // https://docs.microsoft.com/en-us/azure/storage/blobs/storage-blob-rehydration

                var status = hydratedItem.GetProperties().Value.ArchiveStatus;
                if (status == "rehydrate-pending-to-cool" || status == "rehydrate-pending-to-hot")
                    logger.LogInformation($"Hydration pending for {blobToHydrate.Name}");
                else if (status == null)
                    logger.LogInformation($"Hydration done for {blobToHydrate.Name}");
                else
                    throw new ArgumentException("TODO");
            }
        }


        // DELETE

        public void DeleteHydrateFolder()
        {
            logger.LogInformation("Deleting temporary hydration folder");

            foreach (var bi in container.GetBlobs(prefix: RehydratedChunkDirectoryName))
            {
                var bc = container.GetBlobClient(bi.Name);
                bc.Delete();
            }
        }


        // UPLOAD & DOWNLOAD

        /// <summary>
        /// Upload a cleartext stream to the Repository
        /// </summary>
        /// <returns></returns>
        public async Task<ChunkBlobBase> UploadChunkAsync(ChunkHash ch, AccessTier tier, Stream clearStream)
        {
            try
            {
                var bbc = container.GetBlockBlobClient(ch.Value);

                if (await bbc.ExistsAsync())
                    throw new InvalidOperationException();

                // v11 of storage SDK with PutBlock: https://www.andrewhoefling.com/Blog/Post/uploading-large-files-to-azure-blob-storage-in-c-sharp
                // v12 with blockBlob.Upload: https://blog.matrixpost.net/accessing-azure-storage-account-blobs-from-c-applications/

                using (var enc = await bbc.OpenWriteAsync(true))
                {
                    // 7z lacks an encryption salt -- https://crypto.stackexchange.com/a/90140
                    // Good read on 7z, salt and the IV: https://security.stackexchange.com/a/202226
                    // on storing the IV/Salt: https://stackoverflow.com/questions/44694994/storing-iv-when-using-aes-asymmetric-encryption-and-decryption
                    // on storing the IV/Salt: https://stackoverflow.com/a/13915596/1582323

                    // Code derived from: https://docs.microsoft.com/en-us/dotnet/api/system.security.cryptography.aes?view=net-5.0
                    // https://asecuritysite.com/encryption/open_aes?val1=hello&val2=qwerty&val3=241fa86763b85341

                    using var aes = Aes.Create(); //defaults to CBC Mode
                    DeriveBytes(passphrase, out var key, out var iv);
                    aes.Key = key;
                    aes.IV = iv;
                    using var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
                    using var cs = new CryptoStream(enc, encryptor, CryptoStreamMode.Write);

                    // https://docs.microsoft.com/en-us/dotnet/api/system.io.compression.gzipstream?redirectedfrom=MSDN&view=net-5.0#examples
                    // https://stackoverflow.com/a/48192297/1582323
                    // https://stackoverflow.com/questions/3722192/how-do-i-use-gzipstream-with-system-io-memorystream/39157149#39157149
                    // https://docs.microsoft.com/en-us/dotnet/standard/io/how-to-compress-and-extract-files#example-4-compress-and-decompress-gz-files
                    // https://dotnetcodr.com/2015/01/23/how-to-compress-and-decompress-files-with-gzip-in-net-c/

                    using var gz1 = new GZipStream(cs, CompressionLevel.Fastest);

                    await clearStream.CopyToAsync(gz1);
                }

                await bbc.SetAccessTierAsync(tier);

                return ChunkBlobBase.GetChunkBlob(bbc);
            }
            catch (Exception e)
            {
                throw;
            }
        }


        public async Task DownloadChunkAsync(ChunkHash ch, Stream clearStream)
        {
            try
            {
                var bbc = container.GetBlockBlobClient(ch.Value);

                using (var enc = await bbc.OpenReadAsync())
                {
                    using var aes = Aes.Create();
                    DeriveBytes(passphrase, out var key, out var iv);
                    aes.Key = key;
                    aes.IV = iv;
                    using var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
                    using var cs = new CryptoStream(enc, decryptor, CryptoStreamMode.Read);

                    using var gz2 = new GZipStream(cs, CompressionMode.Decompress);

                    await gz2.CopyToAsync(clearStream);
                }
            }
            catch (Exception e)
            {
                throw;
            }
        }

        private static void DeriveBytes(string password, out byte[] key, out byte[] iv)
        {
            //https://docs.microsoft.com/en-us/dotnet/api/system.security.cryptography.rfc2898derivebytes?view=net-5.0

            //var salt = new byte[8];
            //using var rngCsp = new RNGCryptoServiceProvider();
            //rngCsp.GetBytes(salt);

            var salt = SHA256.Create().ComputeHash(Encoding.ASCII.GetBytes(password)); //NOTE for eternity: GuillaumeB sait it will be ok to not use a random salt

            using var derivedBytes = new Rfc2898DeriveBytes(password, salt, 1000);
            key = derivedBytes.GetBytes(32);
            iv = derivedBytes.GetBytes(16);
        }

        public IEnumerable<ChunkBlobBase> Upload(EncryptedChunkFile[] ecfs, AccessTier tier)
        {
            throw new NotImplementedException();

            //_blobCopier.Upload(ecfs, tier, ChunkDirectoryName, false);

            //// Return an IEnumerable - the result may not be needed/materialized by the caller
            //return ecfs.Select(ecf =>
            //{
            //    if (GetChunkBlobByName(ChunkDirectoryName, ecf.Name) is var cb)
            //        return cb;

            //    throw new InvalidOperationException($"Sequence contains no elements - could not create {nameof(ChunkBlobItem)} of uploaded chunk {ecf.Hash}");
            //});
        }

        public IEnumerable<EncryptedChunkFile> Download(ChunkBlobBase[] chunkBlobs, DirectoryInfo target, bool flatten)
        {
            throw new NotImplementedException();

            //var downloadedFiles = _blobCopier.Download(chunkBlobs, target, flatten);

            ////if (chunkBlobs.Count() != downloadedFiles.Count())
            ////    throw new InvalidOperationException("Amount of downloaded files does not match"); //TODO

            //if (chunkBlobs.Select(cb => cb.Name).Except(downloadedFiles.Select(f => f.Name)).Any())
            //    throw new InvalidOperationException("Amount of downloaded files does not match"); //TODO

            //// Return an IEnumerable - the result may not be needed/materialized by the caller
            //// TODO: eliminate ToArray call()
            //return downloadedFiles.Select(fi => new EncryptedChunkFile(fi)).ToArray();

            ////return downloadedFiles.Select(fi2=>
            ////{
            ////    if (new FileInfo(Path.Combine(target.FullName, fi2.Name)) is var fi)
            ////        return new EncryptedChunkFile(null, fi, fi2.Hash);

            ////    throw new InvalidOperationException($"Sequence contains no element - {fi.FullName} should have been downloaded but isn't found on disk");
            ////});
        }
    }






    /* APART 1
     * Zie commits 4608264, d085c83, 0a2523b, 12953c7
     * 
            using (var plain = File.OpenRead(plainFile))
            {
                using var compressedFileStream = File.OpenWrite(compFile);
                using var compressionStream = new GZipStream(compressedFileStream, CompressionLevel.Optimal);
                await plain.CopyToAsync(compressionStream);
            }

            using (var compNotEnc = File.OpenRead(compFile))
            {
                using var notCompNotEnc = File.OpenWrite(uncompFile);
                using var gz2 = new GZipStream(compNotEnc, CompressionMode.Decompress);
                await gz2.CopyToAsync(notCompNotEnc);
                notCompNotEnc.Close();
            }

            using (var plain = File.OpenRead(plainFile))
            {
                using var enc = File.OpenWrite(encFile);
                using var aes = Aes.Create();
                DeriveBytes(password, out var key, out var iv);
                aes.Key = key;
                aes.IV = iv;
                using var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
                using var cs = new CryptoStream(enc, encryptor, CryptoStreamMode.Write);
                await plain.CopyToAsync(cs);
                cs.FlushFinalBlock();
            }

            using (var enc = File.OpenRead(encFile))
            {
                using var notCompNotEnc = File.OpenWrite(decFile);
                using var aes = Aes.Create();
                DeriveBytes(password, out var key, out var iv);
                aes.Key = key;
                aes.IV = iv;
                using var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
                using var cs = new CryptoStream(notCompNotEnc, decryptor, CryptoStreamMode.Write);
                await enc.CopyToAsync(cs);
                cs.FlushFinalBlock();
            }
     * */





    //class Uploader
    //{
    //    private readonly IBlobCopier.IOptions options;

    //    public Uploader(IBlobCopier.IOptions options)
    //    {
    //        this.options = options;

    //        //var bsc = new BlobServiceClient(connectionString);
    //        //container = bsc.GetBlobContainerClient(options.Container);

    //        //var r = container.CreateIfNotExists(PublicAccessType.None);

    //        //if (r is not null && r.GetRawResponse().Status == (int)HttpStatusCode.Created)
    //        //this.logger.LogInformation($"Created container {options.Container}... ");

    //    }

    //    //private readonly BlobContainerClient container;


    //    public async Task UploadChunkAsync(ReadOnlyMemory<byte> chunk, ChunkHash hash)
    //    {
    //        var connectionString = $"DefaultEndpointsProtocol=https;AccountName={options.AccountName};AccountKey={options.AccountKey};EndpointSuffix=core.windows.net";
    //        //var bsc = new BlobContainerClient(connectionString, options.Container);
    //        //await bsc.UploadBlobAsync(hash.Value.ToString(), new BinaryData(chunk));


    //        var x = new BlobClient(connectionString, options.Container, hash.Value.ToString());
    //        await x.UploadAsync(new BinaryData(chunk), new BlobUploadOptions { AccessTier = AccessTier.Cool, TransferOptions = new StorageTransferOptions { MaximumConcurrency = 16 } });
    //        //x.DownloadTo()
    //        public async Task UploadChunkAsync(Stream s, ManifestHash hash)
    //        {
    //            var connectionString = $"DefaultEndpointsProtocol=https;AccountName={options.AccountName};AccountKey={options.AccountKey};EndpointSuffix=core.windows.net";
    //            var x = new BlobClient(connectionString, options.Container, hash.Value.ToString());
    //            await x.UploadAsync(s, new BlobUploadOptions { AccessTier = AccessTier.Cool, TransferOptions = new StorageTransferOptions { MaximumConcurrency = 16 } });

    //            //var bsc = new BlobContainerClient(connectionString, options.Container);
    //            //await bsc.UploadBlobAsync(hash.Value.ToString(), s);

    //        }
    //    }
    //}


    //static async Task<ReadOnlyMemory<byte>> EncryptAsync(ReadOnlyMemory<byte> plain, string password)
    //{
    //    DeriveBytes(password, out var key, out var iv);

    //    using var aesAlg = Aes.Create();
    //    aesAlg.Key = key;
    //    aesAlg.IV = iv;

    //    using var encryptor = aesAlg.CreateEncryptor(aesAlg.Key, aesAlg.IV);

    //    using var to = new MemoryStream();
    //    using var writer = new CryptoStream(to, encryptor, CryptoStreamMode.Write);

    //    await writer.WriteAsync(plain);
    //    writer.FlushFinalBlock();

    //    return to.ToArray().AsMemory();
    //}

    //static async Task<ReadOnlyMemory<byte>> DecryptAsync(ReadOnlyMemory<byte> cipher, string password)
    //{
    //    DeriveBytes(password, out var key, out var iv);

    //    using var aesAlg = Aes.Create();
    //    aesAlg.Key = key;
    //    aesAlg.IV = iv;

    //    using var decryptor = aesAlg.CreateDecryptor(aesAlg.Key, aesAlg.IV);

    //    using var to = new MemoryStream();
    //    using var writer = new CryptoStream(to, decryptor, CryptoStreamMode.Write);

    //    await writer.WriteAsync(cipher);
    //    writer.FlushFinalBlock();

    //    return to.ToArray().AsMemory();
    //}



    //public static ReadOnlyMemory<byte> Compress(ReadOnlyMemory<byte> decompressed, CompressionLevel compressionLevel = CompressionLevel.Fastest) //https://stackoverflow.com/a/39157149/1582323
    //{
    //    var compressed = new MemoryStream();
    //    using (var zip = new GZipStream(compressed, compressionLevel, true))
    //    {
    //        decompressed.AsStream().CopyTo(zip);
    //    }

    //    compressed.Seek(0, SeekOrigin.Begin);
    //    //compressed.GetBuffer().AsSpan()
    //    //var x = (ReadOnlySpan<byte>)compressed.ToArray().AsSpan();
    //    return compressed.ToArray().AsMemory();
    //}




    //public static ReadOnlyMemory<byte> Decompress(ReadOnlyMemory<byte> compressed)
    //{
    //    using var decompressed = new MemoryStream();
    //    using var zip = new GZipStream(compressed.AsStream(), CompressionMode.Decompress, true);

    //    zip.CopyTo(decompressed);

    //    decompressed.Seek(0, SeekOrigin.Begin);
    //    return decompressed.ToArray().AsMemory();
    //}






    //private async Task ProcessAsync(ManifestHash hash, Stream plain, string password)
    //{
    //    var tempFile = Path.GetTempFileName();

    //    //try
    //    //{
    //    using var compressedEncrypted = File.Open(tempFile, FileMode.Open);

    //    using var aes = Aes.Create();
    //    DeriveBytes(password, out var key, out var iv);
    //    aes.Key = key;
    //    aes.IV = iv;
    //    using var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
    //    using var compressedNotEncrypted = new CryptoStream(compressedEncrypted, encryptor, CryptoStreamMode.Write);

    //    using var notCompressedNotEncrypted = new GZipStream(compressedNotEncrypted, CompressionLevel.Optimal, true);

    //    await plain.CopyToAsync(notCompressedNotEncrypted);
    //    compressedNotEncrypted.FlushFinalBlock();

    //    compressedEncrypted.Position = 0;

    //    var connectionString = $"DefaultEndpointsProtocol=https;AccountName={options.AccountName};AccountKey={options.AccountKey};EndpointSuffix=core.windows.net";
    //    var bc = new BlobClient(connectionString, options.Container, hash.Value.ToString());
    //    await bc.UploadAsync(compressedEncrypted, new BlobUploadOptions { AccessTier = AccessTier.Cool, TransferOptions = new StorageTransferOptions { MaximumConcurrency = 16 } });
    //    //}
    //    //finally
    //    //{
    //    //    File.Delete(tempFile);
    //    //}
    //}


    //private async Task<string> UnprocessAsync(ManifestHash hash, string password)
    //{
    //    var compressedEncryptedFile = Path.GetTempFileName();

    //    try
    //    {
    //        var connectionString = $"DefaultEndpointsProtocol=https;AccountName={options.AccountName};AccountKey={options.AccountKey};EndpointSuffix=core.windows.net";
    //        var bc = new BlobClient(connectionString, options.Container, hash.Value.ToString());
    //        await bc.DownloadToAsync(compressedEncryptedFile, transferOptions: new StorageTransferOptions { MaximumConcurrency = 16 });

    //        using var compressedEncrypted = File.OpenRead(compressedEncryptedFile);

    //        using var aes = Aes.Create();
    //        DeriveBytes(password, out var key, out var iv);
    //        aes.Key = key;
    //        aes.IV = iv;
    //        using var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
    //        using var compressedNotEncrypted = new CryptoStream(compressedEncrypted, decryptor, CryptoStreamMode.Read);

    //        using var notCompressedNotEncrypted = new GZipStream(compressedNotEncrypted, CompressionMode.Decompress, true);

    //        var notCompressedNotEncryptedFile = Path.GetTempFileName();
    //        using var notCompressedNotEncryptedFileStream = File.OpenWrite(notCompressedNotEncryptedFile);

    //        //notCompressedNotEncrypted.Position = 0;
    //        await notCompressedNotEncrypted.CopyToAsync(notCompressedNotEncryptedFileStream);
    //        notCompressedNotEncrypted.Flush(); //.FlushFinalBlock();

    //        return notCompressedNotEncryptedFile;
    //    }
    //    catch (Exception e)
    //    {
    //        throw;
    //    }
    //    finally
    //    {
    //        File.Delete(compressedEncryptedFile);
    //    }

    //}
}




