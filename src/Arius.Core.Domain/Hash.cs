namespace Arius.Core.Domain;

internal record Hash // IEquatable is implicitly implemented
{
    public Hash(byte[] value)
    {
        Value = value;
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
}