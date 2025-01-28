using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace Arius.Core.Extensions;

internal static class CryptoExtensions
{
    private const string OPENSSL_SALT_PREFIX = "Salted__";
    private static readonly byte[] OPENSSL_SALT_PREFIX_BYTES = Encoding.ASCII.GetBytes(OPENSSL_SALT_PREFIX);

    private const CipherMode mode = CipherMode.CBC;
    private const PaddingMode padding = PaddingMode.PKCS7; // identical to PKCS5 which is what OpenSSL uses by default https://crypto.stackexchange.com/a/10523
    private const int keySize = 256;
    private const int blockSize = 128;
    private const int saltSize = 8;

    public static async Task CopyToCompressedEncryptedAsync(this Stream source, Stream target, string passphrase, CancellationToken cancellationToken = default)
    {
        DeriveBytes(passphrase, out var salt, out var key, out var iv);
        using var aes = CreateAes(key, iv);
        using var encryptor = aes.CreateEncryptor(/*aes.Key, aes.IV)*/);
        await using var cs = new CryptoStream(target, encryptor, CryptoStreamMode.Write);
        await using var gzs = new GZipStream(cs, CompressionLevel.Optimal);

        await target.WriteAsync(OPENSSL_SALT_PREFIX_BYTES, 0, OPENSSL_SALT_PREFIX_BYTES.Length);
        await target.WriteAsync(salt, 0, salt.Length);

        await source.CopyToAsync(gzs, cancellationToken);
    }

    public static async Task CopyToEncryptedAsync(this Stream source, Stream target, string passphrase, CancellationToken cancellationToken = default)
    {
        DeriveBytes(passphrase, out var salt, out var key, out var iv);
        using var       aes       = CreateAes(key, iv);
        using var       encryptor = aes.CreateEncryptor();
        await using var cs        = new CryptoStream(target, encryptor, CryptoStreamMode.Write);

        // Write OpenSSL-compatible salt prefix and salt
        await target.WriteAsync(OPENSSL_SALT_PREFIX_BYTES, 0, OPENSSL_SALT_PREFIX_BYTES.Length, cancellationToken);
        await target.WriteAsync(salt,                      0, salt.Length,                      cancellationToken);

        // Copy the source stream directly into the CryptoStream (no compression)
        await source.CopyToAsync(cs, bufferSize: 81920, cancellationToken);

        // Ensure all data is flushed and encryption is finalized
        await cs.FlushAsync(cancellationToken);
    }

    public static async Task CopyToDecryptedDecompressedAsync(this Stream source, Stream target, string passphrase, CancellationToken cancellationToken = default)
    {
        var salt = new byte[saltSize];
        source.Seek(OPENSSL_SALT_PREFIX_BYTES.Length, SeekOrigin.Begin);
        source.Read(salt, 0, salt.Length);

        DeriveBytes(passphrase, salt, out var key, out var iv);

        using var aes = CreateAes(key, iv);
        using var decryptor = aes.CreateDecryptor(/*aes.Key, aes.IV*/);
        await using var cs = new CryptoStream(source, decryptor, CryptoStreamMode.Read);

        await using var gzs = new GZipStream(cs, CompressionMode.Decompress);

        await gzs.CopyToAsync(target, cancellationToken);
    }

    //public static async Task<CryptoStream> GetCryptoStreamAsync(this Stream target, string passphrase)
    //{
    //    DeriveBytes(passphrase, out var salt, out var key, out var iv);
    //    using var aes       = CreateAes(key, iv);
    //    using var encryptor = aes.CreateEncryptor(/*aes.Key, aes.IV)*/);

    //    await target.WriteAsync(OPENSSL_SALT_PREFIX_BYTES, 0, OPENSSL_SALT_PREFIX_BYTES.Length);
    //    await target.WriteAsync(salt,                      0, salt.Length);

    //    return new CryptoStream(target, encryptor, CryptoStreamMode.Write);
    //}

    public static async Task<Stream> GetCryptoStreamAsync2(this Stream baseStream, string passphrase, CancellationToken cancellationToken = default)
    {
        // Derive key and IV using the passphrase
        DeriveBytes(passphrase, out var salt, out var key, out var iv);

        // Write OpenSSL-compatible salt prefix and salt to the base stream
        await baseStream.WriteAsync(OPENSSL_SALT_PREFIX_BYTES, 0, OPENSSL_SALT_PREFIX_BYTES.Length, cancellationToken);
        await baseStream.WriteAsync(salt,                      0, salt.Length,                      cancellationToken);

        // Create AES encryptor and crypto stream
        var aes          = CreateAes(key, iv);
        var encryptor    = aes.CreateEncryptor();
        var cryptoStream = new CryptoStream(baseStream, encryptor, CryptoStreamMode.Write, leaveOpen: true);

        // Return a stream wrapper that disposes both the crypto stream and AES instance
        return new DisposableCryptoStream(cryptoStream, aes);
    }

    //private sealed class DisposableCryptoStream : Stream
    //{
    //    private readonly CryptoStream _cryptoStream;
    //    private readonly Aes _aes;
    //    private readonly Stream _baseStream;
    //    private long _virtualPosition;

    //    public DisposableCryptoStream(CryptoStream cryptoStream, Aes aes, Stream baseStream)
    //    {
    //        _cryptoStream = cryptoStream;
    //        _aes = aes;
    //        _baseStream = baseStream;

    //        // Initialize position with salt header bytes already written
    //        _virtualPosition = OPENSSL_SALT_PREFIX_BYTES.Length + saltSize;
    //    }

    //    public override bool CanRead => false;
    //    public override bool CanSeek => false;
    //    public override bool CanWrite => true;
    //    public override long Length => throw new NotSupportedException();

    //    public override long Position
    //    {
    //        get => _baseStream.Position;
    //        set => throw new NotSupportedException();
    //    }

    //    public override void Write(byte[] buffer, int offset, int count)
    //    {
    //        _cryptoStream.Write(buffer, offset, count);
    //        _virtualPosition += count;
    //    }

    //    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    //    {
    //        await _cryptoStream.WriteAsync(buffer, offset, count, cancellationToken);
    //        _virtualPosition += count;
    //    }

    //    public override void Flush() => _cryptoStream.Flush();

    //    public override async Task FlushAsync(CancellationToken cancellationToken) =>
    //        await _cryptoStream.FlushAsync(cancellationToken);

    //    // These operations are not supported for write-only streams
    //    public override int Read(byte[] buffer, int offset, int count) =>
    //        throw new NotSupportedException();
    //    public override long Seek(long offset, SeekOrigin origin) =>
    //        throw new NotSupportedException();
    //    public override void SetLength(long value) =>
    //        throw new NotSupportedException();

    //    protected override void Dispose(bool disposing)
    //    {
    //        if (disposing)
    //        {
    //            _cryptoStream.Dispose();
    //            _aes.Dispose();
    //        }
    //        base.Dispose(disposing);
    //    }

    //    // Add finalization logic if needed
    //}

    private sealed class DisposableCryptoStream : Stream
    {
        private readonly CryptoStream _cryptoStream;
        private readonly Aes _aes;

        public DisposableCryptoStream(CryptoStream cryptoStream, Aes aes)
        {
            _cryptoStream = cryptoStream;
            _aes = aes;
        }

        public override bool CanRead => _cryptoStream.CanRead;
        public override bool CanSeek => _cryptoStream.CanSeek;
        public override bool CanWrite => _cryptoStream.CanWrite;
        public override long Length => _cryptoStream.Length;
        public override long Position
        {
            get => _cryptoStream.Position;
            set => _cryptoStream.Position = value;
        }

        public override void Flush() => _cryptoStream.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => _cryptoStream.FlushAsync(cancellationToken);
        public override int Read(byte[] buffer, int offset, int count) => _cryptoStream.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => _cryptoStream.Seek(offset, origin);
        public override void SetLength(long value) => _cryptoStream.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => _cryptoStream.Write(buffer, offset, count);
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            _cryptoStream.WriteAsync(buffer, offset, count, cancellationToken);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _cryptoStream.Dispose();
                _aes.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    private static Aes CreateAes(byte[] key, byte[] iv)
    {
        var aes = Aes.Create();
        aes.Mode      = mode;
        aes.Padding   = padding;
        aes.KeySize   = keySize;
        aes.BlockSize = blockSize;
        aes.Key       = key;
        aes.IV        = iv;
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