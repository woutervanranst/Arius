using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace ZioFileSystem.AzureBlobStorage;

public static class CryptoService
{
    private const string OPENSSL_SALT_PREFIX = "Salted__";
    private static readonly byte[] OPENSSL_SALT_PREFIX_BYTES = Encoding.ASCII.GetBytes(OPENSSL_SALT_PREFIX);

    private const CipherMode mode = CipherMode.CBC;
    private const PaddingMode padding = PaddingMode.PKCS7; // identical to PKCS5 which is what OpenSSL uses by default https://crypto.stackexchange.com/a/10523
    private const int keySize = 256;
    private const int blockSize = 128;
    private const int saltSize = 8;

    public static async Task CopyToCompressedEncryptedAsync(this Stream source, Stream target, string passphrase)
    {
        DeriveBytes(passphrase, out var salt, out var key, out var iv);
        using var aes = CreateAes(key, iv);
        using var encryptor = aes.CreateEncryptor(/*aes.Key, aes.IV)*/);
        await using var cs = new CryptoStream(target, encryptor, CryptoStreamMode.Write);
        await using var gzs = new GZipStream(cs, CompressionLevel.Optimal);

        await target.WriteAsync(OPENSSL_SALT_PREFIX_BYTES, 0, OPENSSL_SALT_PREFIX_BYTES.Length);
        await target.WriteAsync(salt, 0, salt.Length);

        await source.CopyToAsync(gzs);
    }

    public static async Task CopyToDecryptedDecompressedAsync(this Stream source, Stream target, string passphrase)
    {
        var salt = new byte[saltSize];
        source.Seek(OPENSSL_SALT_PREFIX_BYTES.Length, SeekOrigin.Begin);
        source.Read(salt, 0, salt.Length);

        DeriveBytes(passphrase, salt, out var key, out var iv);

        using var aes = CreateAes(key, iv);
        using var decryptor = aes.CreateDecryptor(/*aes.Key, aes.IV*/);
        await using var cs = new CryptoStream(source, decryptor, CryptoStreamMode.Read);

        await using var gzs = new GZipStream(cs, CompressionMode.Decompress);

        await gzs.CopyToAsync(target);
    }

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
    private static void DeriveBytes(string passphrase, byte[] salt, out byte[] key, out byte[] iv)
    {
        const int iterations = 10_000; //the default openssl implementation is for 10k iterations

        using var pbkdf2 = new Rfc2898DeriveBytes(passphrase, salt, iterations, HashAlgorithmName.SHA256);
        key = pbkdf2.GetBytes(32);
        iv = pbkdf2.GetBytes(16);
    }
}