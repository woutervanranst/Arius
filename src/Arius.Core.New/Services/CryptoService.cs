using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace Arius.Core.New.Services;

public interface ICryptoService
{
    Task CompressAndEncryptAsync(Stream source, Stream target, string passphrase);
    Task DecryptAndDecompressAsync(Stream source, Stream target, string passphrase);
}

public class CryptoService : ICryptoService
{
    private const string OPENSSL_SALT_PREFIX = "Salted__";
    private static readonly byte[] OPENSSL_SALT_PREFIX_BYTES = Encoding.ASCII.GetBytes(OPENSSL_SALT_PREFIX);

    private const CipherMode mode = CipherMode.CBC;
    private const PaddingMode padding = PaddingMode.PKCS7; // identical to PKCS5 which is what OpenSSL uses by default https://crypto.stackexchange.com/a/10523
    private const int keySize = 256;
    private const int blockSize = 128;
    private const int saltSize = 8;

    public static readonly string ContentType = "application/aes256cbc+gzip";

    public async Task CompressAndEncryptAsync(Stream source, Stream target, string passphrase)
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

        DeriveBytes(passphrase, out var salt, out var key, out var iv);
        using var       aes       = CreateAes(key, iv);
        using var       encryptor = aes.CreateEncryptor(/*aes.Key, aes.IV)*/);
        await using var cs        = new CryptoStream(target, encryptor, CryptoStreamMode.Write);


        /* SET UP COMPRESSION
         * 
         * https://docs.microsoft.com/en-us/dotnet/api/system.io.compression.gzipstream?redirectedfrom=MSDN&view=net-5.0#examples
         * https://stackoverflow.com/a/48192297/1582323
         * https://stackoverflow.com/questions/3722192/how-do-i-use-gzipstream-with-system-io-memorystream/39157149#39157149
         * https://docs.microsoft.com/en-us/dotnet/standard/io/how-to-compress-and-extract-files#example-4-compress-and-decompress-gz-files
         * https://dotnetcodr.com/2015/01/23/how-to-compress-and-decompress-files-with-gzip-in-net-c/
         */

        await using var gzs = new GZipStream(cs, CompressionLevel.Optimal);


        // Write salt to the begining of the TARGET stream -- not the gz stream
        await target.WriteAsync(OPENSSL_SALT_PREFIX_BYTES, 0, OPENSSL_SALT_PREFIX_BYTES.Length);
        await target.WriteAsync(salt, 0, salt.Length);

        // Source through GZip through AES to target
        await source.CopyToAsync(gzs);
    }

    public async Task DecryptAndDecompressAsync(Stream source, Stream target, string passphrase)
    {
        // Read the salt from the beginning of the source stream
        var salt = new byte[saltSize];
        source.Seek(OPENSSL_SALT_PREFIX_BYTES.Length, SeekOrigin.Begin);
        source.Read(salt, 0, salt.Length);

        DeriveBytes(passphrase, salt, out var key, out var iv);

        using var       aes       = CreateAes(key, iv);
        using var       decryptor = aes.CreateDecryptor(/*aes.Key, aes.IV*/);
        await using var cs        = new CryptoStream(source, decryptor, CryptoStreamMode.Read);

        await using var gzs = new GZipStream(cs, CompressionMode.Decompress);

        await gzs.CopyToAsync(target);
    }


    //public string Encrypt(string plainText, string passphrase)
    //{
    //    DeriveBytes(passphrase, out var salt, out var key, out var iv);

    //    using var aes = CreateAes(key, iv);
    //    using var encryptor = aes.CreateEncryptor();
    //    using var target = new MemoryStream();
    //    using var cs = new CryptoStream(target, encryptor, CryptoStreamMode.Write);

    //    var plainTextBytes = Encoding.UTF8.GetBytes(plainText);

    //    cs.Write(plainTextBytes, 0, plainTextBytes.Length);
    //    cs.FlushFinalBlock();

    //    var cipherTextBytes = salt.Concat(target.ToArray()).ToArray();
    //    var r = Convert.ToBase64String(cipherTextBytes);

    //    return r;
    //}

    //public string Decrypt(string cipherText, string passphrase)
    //{
    //    var cipherTextBytes = Convert.FromBase64String(cipherText);

    //    DeriveBytes(passphrase, cipherTextBytes[0..8], out var key, out var iv);

    //    using var aes = CreateAes(key, iv);
    //    using var decryptor = aes.CreateDecryptor();
    //    using var source = new MemoryStream(cipherTextBytes[8..]);
    //    using var cs = new CryptoStream(source, decryptor, CryptoStreamMode.Read);
    //    using var target = new MemoryStream();
    //    using var sr = new StreamReader(cs, Encoding.UTF8);

    //    var plainText = sr.ReadToEnd();

    //    return plainText;
    //}

    private static Aes CreateAes(byte[] key, byte[] iv)
    {
        var aes = Aes.Create();
        aes.Mode = mode;
        aes.Padding = padding;
        aes.KeySize = keySize;
        aes.BlockSize = blockSize;
        aes.Key = key;
        aes.IV = iv;
        return aes;
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

        salt = new byte[saltSize];
        using var rng = RandomNumberGenerator.Create();
        rng.GetNonZeroBytes(salt);

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