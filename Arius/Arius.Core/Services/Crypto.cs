using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Arius.Core.Services
{
    internal static class Crypto
    {
        private const string OPENSSL_SALT_PREFIX = "Salted__";
        private static readonly byte[] OPENSSL_SALT_PREFIX_BYTES = Encoding.ASCII.GetBytes(OPENSSL_SALT_PREFIX);

        public static async Task CompressAndEncrypt(Stream source, Stream target, string passphrase)
        {
            /* SET UP ENCRYPTION
             * 
             * On 7z and the encryption
             *      7z lacks an encryption salt -- https://crypto.stackexchange.com/a/90140
             *      Good read on 7z, salt and the IV: https://security.stackexchange.com/a/202226
             *      
             * Code derived from: https://docs.microsoft.com/en-us/dotnet/api/system.security.cryptography.aes?view=net-5.0
             * 
             * OpenSSL Compatible encryption, see https://stackoverflow.com/questions/68391070/decrypt-aes-256-cbc-with-pbkdf2-from-openssl-in-c-sharp
             *      Stream starts with 'Salted__' in ASCII
             *      Then 8 bytes of salt (random)
             *      The Key and IV are derived from the passphrase
             *      Also: https://asecuritysite.com/encryption/open_aes?val1=hello&val2=qwerty&val3=241fa86763b85341
             *      On OpenSSL options and the kdf: https://crypto.stackexchange.com/a/35614
             *  
             *  Additional references
             *      https://github.com/Nicholi/OpenSSLCompat -- but this uses a deprecated Key Derivation Function
             *      on storing the IV/Salt: https://stackoverflow.com/questions/44694994/storing-iv-when-using-aes-asymmetric-encryption-and-decryption
             *      on storing the IV/Salt: https://stackoverflow.com/a/13915596/1582323
             */

            using var aes = Aes.Create();
            DeriveBytes(passphrase, out var salt, out var key, out var iv);
            aes.Mode = CipherMode.CBC;
            aes.KeySize = 256;
            aes.BlockSize = 128;
            aes.Padding = PaddingMode.PKCS7; // identical to PKCS5 which is what OpenSSL uses by default https://crypto.stackexchange.com/a/10523
            aes.Key = key;
            aes.IV = iv;

            using var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
            using var cs = new CryptoStream(target, encryptor, CryptoStreamMode.Write);


            /* SET UP COMPRESSION
             * 
             * https://docs.microsoft.com/en-us/dotnet/api/system.io.compression.gzipstream?redirectedfrom=MSDN&view=net-5.0#examples
             * https://stackoverflow.com/a/48192297/1582323
             * https://stackoverflow.com/questions/3722192/how-do-i-use-gzipstream-with-system-io-memorystream/39157149#39157149
             * https://docs.microsoft.com/en-us/dotnet/standard/io/how-to-compress-and-extract-files#example-4-compress-and-decompress-gz-files
             * https://dotnetcodr.com/2015/01/23/how-to-compress-and-decompress-files-with-gzip-in-net-c/
             */

            using var gzs = new GZipStream(cs, CompressionLevel.Fastest);


            // Write salt to the begining of the TARGET stream -- not the gz stream
            target.Write(OPENSSL_SALT_PREFIX_BYTES, 0, OPENSSL_SALT_PREFIX_BYTES.Length);
            target.Write(salt, 0, salt.Length);

            // Source through Gzip through AES to target
            await source.CopyToAsync(gzs);
        }


        public static async Task DecryptAndDecompress(Stream source, Stream target, string passphrase)
        {
            // Read the salt from the beginning of the source stream
            var salt = new byte[8];
            source.Seek(OPENSSL_SALT_PREFIX_BYTES.Length, SeekOrigin.Begin);
            source.Read(salt, 0, salt.Length);


            using var aes = Aes.Create();
            DeriveBytes(passphrase, salt, out var key, out var iv);
            aes.Mode = CipherMode.CBC;
            aes.KeySize = 256;
            aes.BlockSize = 128;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key = key;
            aes.IV = iv;

            using var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
            using var cs = new CryptoStream(source, decryptor, CryptoStreamMode.Read);

            using var gzs = new GZipStream(cs, CompressionMode.Decompress);

            await gzs.CopyToAsync(target);
        }


        /// <summary>
        /// Get the bytes for AES
        /// Generate a random salt and calculate the key and iv with that salt
        /// </summary>
        /// <param name="password"></param>
        /// <param name="salt"></param>
        /// <param name="key"></param>
        /// <param name="iv"></param>
        private static void DeriveBytes(string password, out byte[] salt, out byte[] key, out byte[] iv)
        {
            //https://docs.microsoft.com/en-us/dotnet/api/system.security.cryptography.rfc2898derivebytes?view=net-5.0

            salt = new byte[8];
            using var rngCsp = new RNGCryptoServiceProvider();
            rngCsp.GetNonZeroBytes(salt);

            DeriveBytes(password, salt, out key, out iv);
        }

        /// <summary>
        /// Get the key and iv for AES based on the given password and salt
        /// </summary>
        /// <param name="passphrase"></param>
        /// <param name="salt"></param>
        /// <param name="key"></param>
        /// <param name="iv"></param>
        private static void DeriveBytes(string passphrase, byte[] salt, out byte[] key, out byte[] iv)
        {
            const int iterations = 10_000; //the default openssl implementation is for 10k iterations

            using var pbkdf2 = new Rfc2898DeriveBytes(passphrase, salt, iterations, HashAlgorithmName.SHA256);
            key = pbkdf2.GetBytes(32);
            iv = pbkdf2.GetBytes(16);
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
