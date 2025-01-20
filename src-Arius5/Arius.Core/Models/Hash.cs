using System.Collections.Immutable;
using WouterVanRanst.Utils.Extensions;

namespace Arius.Core.Models;

public record Hash
{
    public Hash(byte[] value)
    {
        Value = value;
    }

    public Hash(string hexString) : this(hexString.HexStringToBytes())
    {
    }

    public byte[] Value { get; }



    public override int GetHashCode()
    {
        return BitConverter.ToInt32(Value); //return HashCode.Combine(Value); <-- this doesnt work for bytes
    }

    public virtual bool Equals(Hash? other)
    {
        if (other is null)
            return false;

        return Value.AsSpan().SequenceEqual(other.Value.AsSpan());
    }

    public override string ToString() => ToLongString();

    /// <summary>`
    /// Print the first 8 characters of the value
    /// </summary>
    /// <returns></returns>
    public string ToShortString() => Value[..4].BytesToHexString();

    public string ToLongString() => Value.BytesToHexString();

    public static implicit operator Hash(byte[] hash)      => new(hash);
    public static implicit operator Hash(string hexString) => new(hexString.StringToBytes());
}



public record HashValue
{
    private readonly ImmutableArray<byte> bytes;

    private HashValue(ImmutableArray<byte> bytes)
    {
        const int ExpectedByteLength = 32; // SHA256 produces 32-byte hashes

        if (bytes.Length != ExpectedByteLength)
            throw new ArgumentException($"Hash must be exactly {ExpectedByteLength} bytes long.", nameof(bytes));

        this.bytes = bytes;
    }

    /// <summary>
    /// Creates a HashValue from a byte array.
    /// </summary>
    /// <param name="bytes">Byte array representing the hash.</param>
    /// <returns>A new HashValue instance.</returns>
    public static HashValue FromBytes(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        return new HashValue([..bytes]);
    }

    /// <summary>
    /// Creates a HashValue from a hexadecimal string.
    /// </summary>
    /// <param name="hex">Hexadecimal string representing the hash.</param>
    /// <returns>A new HashValue instance.</returns>
    public static HashValue FromHex(string hex)
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
    /// Implicitly converts a byte array to a HashValue.
    /// </summary>
    /// <param name="bytes">Byte array representing the hash.</param>
    public static implicit operator HashValue(byte[] bytes) => FromBytes(bytes);

    /// <summary>
    /// Implicitly converts a hexadecimal string to a HashValue.
    /// </summary>
    /// <param name="hex">Hexadecimal string representing the hash.</param>
    public static implicit operator HashValue(string hex) => FromHex(hex);

    /// <summary>
    /// Gets the byte array representation of the hash.
    /// </summary>
    public static implicit operator byte[](HashValue hash) => hash.bytes.ToArray();

    public static implicit operator ReadOnlySpan<byte>(HashValue hash) => hash.bytes.AsSpan();


    // -- EQUALITY

    public override int GetHashCode()
    {
        return BitConverter.ToInt32(bytes.AsSpan()); //return HashCode.Combine(Value); <-- this doesnt work for bytes
    }

    public virtual bool Equals(HashValue? other)
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

