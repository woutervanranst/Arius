namespace Arius.Core.Domain.Services;

public interface ICryptoService
{
    public const string ContentType = "application/aes256cbc+gzip";

    Task CompressAndEncryptAsync(Stream source, Stream target, string passphrase);
    Task DecryptAndDecompressAsync(Stream source, Stream target, string passphrase);
}