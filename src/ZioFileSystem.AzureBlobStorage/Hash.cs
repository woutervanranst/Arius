using System;
using WouterVanRanst.Utils.Extensions;

namespace ZioFileSystem.AzureBlobStorage;

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

    public static implicit operator Hash(byte[] hash)
    {
        return new Hash(hash);
    }
}