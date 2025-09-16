using Arius.Core.Shared.FileSystem;
using System.Buffers;
using System.Security.Cryptography;
using System.Text;

namespace Arius.Core.Shared.Hashing;

internal sealed class Sha256Hasher : IDisposable
{
    private const int BufferSize = 81920; // 80 KB buffer

    private readonly byte[] saltBytes;
    private readonly ThreadLocal<SHA256> sha256 = new(SHA256.Create);

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

    /// <summary>
    /// Gets the salted hash of a raw byte array. Returns a Task for consistency.
    /// </summary>
    public Task<Hash> GetHashAsync(byte[] data)
    {
        // No I/O, so no real async needed. We keep a Task-based signature
        // to match the rest of the code.
        var hashValue = ComputeSaltedHash(data);
        return Task.FromResult(hashValue);
    }

    public async Task<Hash> GetHashAsync(Stream s)
    {
        return await ComputeSaltedHashAsync(s).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets the salted hash of a FilePair. If it is PointerFileOnly, we simply
    /// return the hash stored in the pointer file. Otherwise, we hash the BinaryFile.
    /// </summary>
    public async Task<Hash> GetHashAsync(FilePair fp)
    {
        if (fp.Type == FilePairType.PointerFileOnly)
            return fp.PointerFile.ReadHash();

        return await GetHashAsync(fp.BinaryFile).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets the salted hash of a BinaryFile. Throws if the file does not exist.
    /// </summary>
    public async Task<Hash> GetHashAsync(BinaryFile bf)
    {
        if (!bf.Exists)
            throw new ArgumentException("BinaryFile does not exist", nameof(bf));

        await using var fs = bf.OpenRead();

        return await ComputeSaltedHashAsync(fs).ConfigureAwait(false);
    }

    /// <summary>
    /// Dispose of the underlying resources.
    /// </summary>
    public void Dispose()
    {
        if (sha256.IsValueCreated)
            sha256.Value.Dispose();

        sha256.Dispose();
    }


    // --- Private Helpers

    /// <summary>
    /// Read the file stream in chunks and compute a salted SHA-256. Salt is prepended.
    /// </summary>
    private async Task<Hash> ComputeSaltedHashAsync(Stream stream)
    {
        var localSha = sha256.Value;
        localSha.Initialize();

        // 1) Salt first
        localSha.TransformBlock(saltBytes, 0, saltBytes.Length, null, 0);

        // 2) File contents
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            int bytesRead;
            while ((bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length)).ConfigureAwait(false)) > 0)
            {
                localSha.TransformBlock(buffer, 0, bytesRead, null, 0);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        // 3) Finalize
        localSha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return localSha.Hash!;
    }

    /// <summary>
    /// Hash a byte array in memory with prepended salt (synchronous).
    /// </summary>
    private Hash ComputeSaltedHash(byte[] data)
    {
        var localSha = sha256.Value;
        localSha.Initialize();

        // 1) Salt
        localSha.TransformBlock(saltBytes, 0, saltBytes.Length, null, 0);

        // 2) Data
        localSha.TransformBlock(data, 0, data.Length, null, 0);

        // 3) Final
        localSha.TransformFinalBlock([], 0, 0);
        return localSha.Hash!;
    }
}
