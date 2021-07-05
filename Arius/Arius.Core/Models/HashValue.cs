using Arius.Core.Extensions;
using System;

namespace Arius.Core.Models
{
    internal abstract record Hash
    {
        public Hash(string value)
        {
            Value = value;
        }
        // TODO implement like https://github.com/Azure/azure-sdk-for-net/blob/master/sdk/storage/Azure.Storage.Blobs/src/Generated/Models/AccessTier.cs

        public string Value { get; }

        //public override string ToString() => Value;

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
            return HashCode.Combine(Value);
        }

        //public override bool Equals(object obj)
        //{
        //    // If parameter is null return false.
        //    if (obj == null)
        //        return false;

        //    // If parameter cannot be cast to HashValue return false.
        //    if (obj is not HashValue)
        //        return false;

        //    // Return true if the fields match:
        //    return Equals((HashValue)obj);
        //}

        //public bool Equals(HashValue obj)
        //{
        //    return Value == obj.Value;
        //}
    }

    internal record ManifestHash : Hash
    {
        public ManifestHash(string value) : base(value)
        { 
        }
    }
    internal record ChunkHash : Hash
    {
        public ChunkHash(string value) : base(value)
        {
        }
        public ChunkHash(ManifestHash manifestHash) : base(manifestHash.Value)
        { 
        }
    }
}



