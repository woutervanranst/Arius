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

    public static async Task<Stream> GetDecryptionStreamAsync(this Stream source, string passphrase, CancellationToken cancellationToken = default)
    {
        var salt = new byte[saltSize];
        source.Seek(OPENSSL_SALT_PREFIX_BYTES.Length, SeekOrigin.Begin);
        source.Read(salt, 0, salt.Length);

        DeriveBytes(passphrase, salt, out var key, out var iv);

        var aes          = CreateAes(key, iv);
        var decryptor    = aes.CreateDecryptor();
        var cryptoStream = new CryptoStream(source, decryptor, CryptoStreamMode.Read, leaveOpen: true);

        var gzs = new GZipStream(cryptoStream, CompressionMode.Decompress, leaveOpen: true);

        return new DisposableDecompressionStream(gzs, cryptoStream, aes);
    }

    public static async Task<Stream> GetCryptoStreamAsync(this Stream source, string passphrase, CancellationToken cancellationToken = default)
    {
        // Derive key and IV using the passphrase
        DeriveBytes(passphrase, out var salt, out var key, out var iv);

        // Write OpenSSL-compatible salt prefix and salt to the base stream
        await source.WriteAsync(OPENSSL_SALT_PREFIX_BYTES, 0, OPENSSL_SALT_PREFIX_BYTES.Length, cancellationToken);
        await source.WriteAsync(salt,                      0, salt.Length,                      cancellationToken);

        // Create AES encryptor and crypto stream
        var aes          = CreateAes(key, iv);
        var encryptor    = aes.CreateEncryptor();
        var cryptoStream = new CryptoStream(source, encryptor, CryptoStreamMode.Write, leaveOpen: true);

        // Return a stream wrapper that disposes both the crypto stream and AES instance
        return new DisposableCryptoStream(cryptoStream, aes);
    }

    private sealed class DisposableCryptoStream : Stream
    {
        private readonly CryptoStream _cryptoStream;
        private readonly Aes          _aes;

        public DisposableCryptoStream(CryptoStream cryptoStream, Aes aes)
        {
            _cryptoStream = cryptoStream;
            _aes = aes;
        }

        public override bool CanRead => _cryptoStream.CanRead;
        public override bool CanSeek => _cryptoStream.CanSeek;
        public override bool CanWrite => _cryptoStream.CanWrite;
        public override long Length   => _cryptoStream.Length;

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

    private sealed class DisposableDecompressionStream : Stream
    {
        private readonly GZipStream   _gzipStream;
        private readonly CryptoStream _cryptoStream;
        private readonly Aes          _aes;

        public DisposableDecompressionStream(GZipStream gzipStream, CryptoStream cryptoStream, Aes aes)
        {
            _gzipStream   = gzipStream;
            _cryptoStream = cryptoStream;
            _aes          = aes;
        }

        public override bool CanRead  => _gzipStream.CanRead;
        public override bool CanSeek  => _gzipStream.CanSeek;
        public override bool CanWrite => _gzipStream.CanWrite;
        public override long Length   => _gzipStream.Length;

        public override long Position
        {
            get => _gzipStream.Position;
            set => _gzipStream.Position = value;
        }

        public override void Flush()                                         => _gzipStream.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => _gzipStream.FlushAsync(cancellationToken);
        public override int  Read(byte[] buffer, int offset, int count)      => _gzipStream.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin)            => _gzipStream.Seek(offset, origin);
        public override void SetLength(long value)                           => _gzipStream.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count)     => _gzipStream.Write(buffer, offset, count);

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            _gzipStream.WriteAsync(buffer, offset, count, cancellationToken);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _gzipStream.Dispose();
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