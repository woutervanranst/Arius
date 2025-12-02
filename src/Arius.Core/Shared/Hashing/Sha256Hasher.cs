using Arius.Core.Shared.FileSystem;
using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace Arius.Core.Shared.Hashing;

internal interface ISha256Hasher
{
    /// <summary>
    /// Gets the salted hash of a raw byte array.
    /// </summary>
    Task<Hash> GetHashAsync(byte[] data, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the salted hash of a Stream.
    /// </summary>
    /// <param name="s"></param>
    /// <returns></returns>
    Task<Hash> GetHashAsync(Stream s, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the salted hash of a FilePair. If it is PointerFileOnly, we simply
    /// return the hash stored in the pointer file. Otherwise, we hash the BinaryFile.
    /// </summary>
    Task<Hash> GetHashAsync(FilePair fp, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the salted hash of a BinaryFile. Throws if the file does not exist.
    /// </summary>
    Task<Hash> GetHashAsync(BinaryFile bf, CancellationToken cancellationToken = default);
}

internal sealed class Sha256Hasher : ISha256Hasher
{
    private const int BufferSize = 81920; // 80 KB buffer
    private readonly byte[] saltBytes;

    /// <summary>
    /// Constructs a Sha256Hasher with a given salt in string form.
    /// </summary>
    /// <param name="salt">ASCII-encoded salt.</param>
    public Sha256Hasher(string salt) => saltBytes = Encoding.ASCII.GetBytes(salt);

    ///// <summary>
    ///// Constructs a Sha256Hasher with a given salt in byte form.
    ///// </summary>
    ///// <param name="salt">Raw salt bytes.</param>
    //private Sha256Hasher(byte[] salt) => saltBytes = salt;

    public Task<Hash> GetHashAsync(byte[] data, CancellationToken cancellationToken = default) // NOTE: returns a Task for consistency
    {
        cancellationToken.ThrowIfCancellationRequested();

        // No I/O, so no real async needed. We keep a Task-based signature to match the rest of the code.
        var hashValue = ComputeSaltedHash(data);
        return Task.FromResult(hashValue);
    }

    public async Task<Hash> GetHashAsync(Stream s, CancellationToken cancellationToken = default)
    {
        return await ComputeSaltedHashAsync(s, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets the salted hash of a FilePair. If it is PointerFileOnly, we simply
    /// return the hash stored in the pointer file. Otherwise, we hash the BinaryFile.
    /// </summary>
    public async Task<Hash> GetHashAsync(FilePair fp, CancellationToken cancellationToken = default)
    {
        if (fp.Type == FilePairType.PointerFileOnly)
            return fp.PointerFile.ReadHash();

        return await GetHashAsync(fp.BinaryFile, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets the salted hash of a BinaryFile. Throws if the file does not exist.
    /// </summary>
    public async Task<Hash> GetHashAsync(BinaryFile bf, CancellationToken cancellationToken = default)
    {
        if (!bf.Exists)
            throw new ArgumentException("BinaryFile does not exist", nameof(bf));

        cancellationToken.ThrowIfCancellationRequested();

        await using var fs = bf.OpenRead();

        return await ComputeSaltedHashAsync(fs, cancellationToken).ConfigureAwait(false);
    }

    // --- Private helpers (per-operation SHA256) ---

    private async Task<Hash> ComputeSaltedHashAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var sha = SHA256.Create();
        sha.Initialize();

        // 1) Salt first
        sha.TransformBlock(saltBytes, 0, saltBytes.Length, null, 0);

        // 2) Stream contents
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            int bytesRead;
            while ((bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
            {
                sha.TransformBlock(buffer, 0, bytesRead, null, 0);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        // 3) Finalize
        sha.TransformFinalBlock([], 0, 0);
        return sha.Hash!;
    }

    private Hash ComputeSaltedHash(byte[] data)
    {
        using var sha = SHA256.Create();
        sha.Initialize();

        // 1) Salt
        sha.TransformBlock(saltBytes, 0, saltBytes.Length, null, 0);

        // 2) Data
        sha.TransformBlock(data, 0, data.Length, null, 0);

        // 3) Final
        sha.TransformFinalBlock([], 0, 0);
        return sha.Hash!;
    }
}
