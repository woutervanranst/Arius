using WouterVanRanst.Utils.Extensions;

namespace Arius.Core.DbMigrationV3V5.v3;

internal abstract record Hash
{
    public Hash(byte[] value)
    {
        Value = value;
    }

    public byte[] Value { get; }

    public sealed override string ToString() => ToShortString(); // marked sealed since records require re-overwriting https://stackoverflow.com/a/64094532/1582323

    /// <summary>`
    /// Print the first 8 characters of the value
    /// </summary>
    /// <returns></returns>
    private /* marked as private to discourage use */ string ToShortString() => Value[..4].BytesToHexString();

    private /* marked as private to discourage use */ string ToLongString() => Value.BytesToHexString();

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
}

internal record ChunkHash : Hash
{
    public ChunkHash(byte[] value) : base(value)
    {
    }
}

internal record BinaryHash : ChunkHash // every BinaryHash is a ChunkHash
{
    public BinaryHash(byte[] value) : base(value)
    {
    }
}
