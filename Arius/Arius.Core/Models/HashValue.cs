using Arius.Core.Extensions;
using System;

namespace Arius.Core.Models;

internal abstract record Hash
{
    public Hash(string value)
    {
        Value = value;
    }
    // TODO implement like https://github.com/Azure/azure-sdk-for-net/blob/master/sdk/storage/Azure.Storage.Blobs/src/Generated/Models/AccessTier.cs

    public string Value { get; }

    public override string ToString() => Value;

    /// <summary>
    /// Print the first 8 characters of the value
    /// </summary>
    /// <returns></returns>
    public string ToShortString() => Value.Left(8);


    //public static bool operator ==(HashValue c1, HashValue c2)
    //{
    //    return c1.Equals(c2);
    //}

    //public static bool operator !=(HashValue c1, HashValue c2)
    //{
    //    return !c1.Equals(c2);
    //}

    public override int GetHashCode()
    {
        return Value.GetHashCode(); // HashCode.Combine(Value);
    }

    //public override bool Equals(object obj)
    //{
    //    // If parameter is null return false.
    //    if (obj == null)
    //        return false;

    //    // If parameter cannot be cast to HashValue return false.
    //    if (obj is not Hash)
    //        return false;

    //    // Return true if the fields match:
    //    return Equals((Hash)obj);
    //}

    //public virtual bool Equals(Hash obj)
    //{
    //    return Value == obj.Value;
    //}
}

internal record BinaryHash : Hash
{
    public BinaryHash(string value) : base(value)
    { 
    }
#pragma warning disable S1185 // Overriding members should do more than simply call the same member in the base class
    // This is required in the specific case of a record - see https://stackoverflow.com/a/64094532/1582323
    public override string ToString() => base.ToString();
#pragma warning restore S1185 // Overriding members should do more than simply call the same member in the base class
}
internal record ChunkHash : Hash
{
    public ChunkHash(string value) : base(value)
    {
    }
    public ChunkHash(BinaryHash binaryHash) : base(binaryHash.Value)
    { 
    }
#pragma warning disable S1185 // Overriding members should do more than simply call the same member in the base class
    // This is required in the specific case of a record - see https://stackoverflow.com/a/64094532/1582323
    public override string ToString() => base.ToString();
#pragma warning restore S1185 // Overriding members should do more than simply call the same member in the base class

}