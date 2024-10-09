using Arius.Core.Domain.Services;

namespace Arius.Core.Tests.Fakes;

internal class FakeCryptoService : ICryptoService
{
    public async Task CompressAndEncryptAsync(Stream source, Stream target, string passphrase)
    {
        source ??= Stream.Null;

        // Just copy the source stream to the target stream for mocking purposes
        await source.CopyToAsync(target);
    }

    public async Task DecryptAndDecompressAsync(Stream source, Stream target, string passphrase)
    {
        source ??= Stream.Null;

        // Just copy the source stream to the target stream for mocking purposes
        await source.CopyToAsync(target);
    }
}