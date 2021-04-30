using System;
using System.Collections.Generic;
using Arius.Repositories;

namespace Arius.Extensions
{
    internal class PointerFileEntryEqualityComparer : IEqualityComparer<AzureRepository.PointerFileEntry>
    {
        public bool Equals(AzureRepository.PointerFileEntry x, AzureRepository.PointerFileEntry y)
        {
            return x.RelativeName == y.RelativeName &&
                   // x.Version.Equals(y.Version) && //DO NOT Compare on DateTime Version, we'd by inserting new PointerFileEntries just for the version
                   x.IsDeleted == y.IsDeleted &&
                   //x.CreationTimeUtc.Equals(y.CreationTimeUtc) &&
                    /*
                        * Commenting out comparison on creationtime - this fails on docker on Synology (setting the value has no effect)
                        *      Works on Windows
                        *      Works on ubuntu (github runner)
                        *      Doesn't work in docker / on synology
                        */
                   x.LastWriteTimeUtc.Equals(y.LastWriteTimeUtc);
        }

        public int GetHashCode(AzureRepository.PointerFileEntry obj)
        {
            return HashCode.Combine(obj.RelativeName,
                //obj.Version,  //DO NOT Compare on DateTime Version
                obj.IsDeleted,
                //obj.CreationTimeUtc,
                obj.LastWriteTimeUtc);
        }
    }

}