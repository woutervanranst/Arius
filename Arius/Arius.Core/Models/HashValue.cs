using System;

namespace Arius.Core.Models
{
    public struct HashValue
    {
        // TODO implement like https://github.com/Azure/azure-sdk-for-net/blob/master/sdk/storage/Azure.Storage.Blobs/src/Generated/Models/AccessTier.cs

        public string Value { get; init; }
        public override string ToString() => Value;


        public static bool operator ==(HashValue c1, HashValue c2)
        {
            return c1.Equals(c2);
        }

        public static bool operator !=(HashValue c1, HashValue c2)
        {
            return !c1.Equals(c2);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Value);
        }

        public override bool Equals(object obj)
        {
            // If parameter is null return false.
            if (obj == null)
                return false;

            // If parameter cannot be cast to HashValue return false.
            if (obj is not HashValue)
                return false;

            // Return true if the fields match:
            return Equals((HashValue)obj);
        }

        public bool Equals(HashValue obj)
        {
            return Value == obj.Value;
        }
    }
}



