namespace Arius.Core.Domain;

public record Hash
{
    public Hash(byte[] value)
    {
        Value = value;
    }

    public byte[] Value { get; }
}