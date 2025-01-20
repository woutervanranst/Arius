using System.Collections.Immutable;
using WouterVanRanst.Utils.Extensions;

namespace Arius.Core.Models;

public record Hash
{
    private readonly ImmutableArray<byte> bytes;

    private Hash(ImmutableArray<byte> bytes)
    {
        const int ExpectedByteLength = 32; // SHA256 produces 32-byte hashes

        if (bytes.Length != ExpectedByteLength)
            throw new ArgumentException($"Hash must be exactly {ExpectedByteLength} bytes long.", nameof(bytes));

        this.bytes = bytes;
    }

    /// <summary>
    /// Creates a Hash from a byte array.
    /// </summary>
    /// <param name="bytes">Byte array representing the hash.</param>
    /// <returns>A new Hash instance.</returns>
    public static Hash FromBytes(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        return new Hash([..bytes]);
    }

    /// <summary>
    /// Creates a Hash from a hexadecimal string.
    /// </summary>
    /// <param name="hex">Hexadecimal string representing the hash.</param>
    /// <returns>A new Hash instance.</returns>
    public static Hash FromHex(string hex)
    {
        ArgumentNullException.ThrowIfNull(hex);

        byte[] bytes;
        try
        {
            bytes = Convert.FromHexString(hex);
        }
        catch (FormatException ex)
        {
            throw new ArgumentException("Invalid hexadecimal string.", nameof(hex), ex);
        }

        return FromBytes(bytes);
    }

    // -- IMPLICIT OPERATORS

    /// <summary>
    /// Implicitly converts a byte array to a Hash.
    /// </summary>
    /// <param name="bytes">Byte array representing the hash.</param>
    public static implicit operator Hash(byte[] bytes) => FromBytes(bytes);

    /// <summary>
    /// Implicitly converts a hexadecimal string to a Hash.
    /// </summary>
    /// <param name="hex">Hexadecimal string representing the hash.</param>
    public static implicit operator Hash(string hex) => FromHex(hex);

    /// <summary>
    /// Gets the byte array representation of the hash.
    /// </summary>
    public static implicit operator byte[](Hash hash) => hash.bytes.ToArray();

    public static implicit operator ReadOnlySpan<byte>(Hash hash) => hash.bytes.AsSpan();


    // -- EQUALITY

    public override int GetHashCode()
    {
        return BitConverter.ToInt32(bytes.AsSpan()); //return HashCode.Combine(Value); <-- this doesnt work for bytes
    }

    public virtual bool Equals(Hash? other)
    {
        if (other is null)
            return false;

        return bytes.SequenceEqual(other.bytes);
    }


    // -- TOSTRING

    /// <summary>
    /// Returns the hexadecimal string representation of the hash.
    /// </summary>
    /// <returns>Hexadecimal string.</returns>
    public override string ToString() => Convert.ToHexString(bytes.AsSpan()).ToLowerInvariant();

    public string ToShortString() => Convert.ToHexString(bytes[..4].AsSpan()).ToLowerInvariant();
}

