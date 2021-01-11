using System;
using System.Collections.Generic;
using Arius.Models;

namespace Arius.Extensions
{
    internal class PointerFileEntryEqualityComparer : IEqualityComparer<PointerFileEntry>
    {
        public bool Equals(PointerFileEntry x, PointerFileEntry y)
        {
            return x.RelativeName == y.RelativeName &&
                   //x.Version.Equals(y.Version) && //DO NOT Compare on DateTime Version
                   x.IsDeleted == y.IsDeleted &&
                   x.CreationTimeUtc.Equals(y.CreationTimeUtc) &&
                   x.LastWriteTimeUtc.Equals(y.LastWriteTimeUtc);
        }

        public int GetHashCode(PointerFileEntry obj)
        {
            return HashCode.Combine(obj.RelativeName,
                //obj.Version,  //DO NOT Compare on DateTime Version
                obj.IsDeleted,
                obj.CreationTimeUtc,
                obj.LastWriteTimeUtc);
        }
    }
}