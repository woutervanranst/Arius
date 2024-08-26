namespace Arius.Core.Domain.Services;

public interface ICryptoService
{
    Task CompressAndEncryptAsync(Stream source, Stream target, string passphrase);
    Task DecryptAndDecompressAsync(Stream source, Stream target, string passphrase);
}