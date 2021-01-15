using System;
using System.Collections.Generic;
using Arius.Repositories;

namespace Arius.Extensions
{
    internal class PointerFileEntryEqualityComparer : IEqualityComparer<AzureRepository.PointerFileEntry2>
    {
        public bool Equals(AzureRepository.PointerFileEntry2 x, AzureRepository.PointerFileEntry2 y)
        {
            return x.RelativeName == y.RelativeName &&
                   //x.Version.Equals(y.Version) && //DO NOT Compare on DateTime Version
                   x.IsDeleted == y.IsDeleted &&
                   x.CreationTimeUtc.Equals(y.CreationTimeUtc) &&
                   x.LastWriteTimeUtc.Equals(y.LastWriteTimeUtc);
        }

        public int GetHashCode(AzureRepository.PointerFileEntry2 obj)
        {
            return HashCode.Combine(obj.RelativeName,
                //obj.Version,  //DO NOT Compare on DateTime Version
                obj.IsDeleted,
                obj.CreationTimeUtc,
                obj.LastWriteTimeUtc);
        }
    }

}