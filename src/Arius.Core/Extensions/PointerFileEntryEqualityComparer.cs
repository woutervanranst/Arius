using System;
using System.Collections.Generic;
using Arius.Core.Models;
using Arius.Core.Repositories.StateDb;

namespace Arius.Core.Extensions;

internal class PointerFileEntryEqualityComparer : IEqualityComparer<PointerFileEntry>
{
    public bool Equals(PointerFileEntry x, PointerFileEntry y)
    {
        if (x is null && y is null)
            throw new ArgumentException("Both PointerFileEntries are null");
        if (x is null || y is null)
            return false;

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

    public int GetHashCode(PointerFileEntry obj)
    {
        return HashCode.Combine(obj.RelativeName,
            //obj.Version,  //DO NOT Compare on DateTime Version
            obj.IsDeleted,
            //obj.CreationTimeUtc,
            obj.LastWriteTimeUtc);
    }
}

//internal class PointerFileEntryDtoEqualityComparer : IEqualityComparer<PointerFileEntryDto>
//{
//    public bool Equals(PointerFileEntryDto x, PointerFileEntryDto y)
//    {
//        if (x is null && y is null)
//            throw new ArgumentException("Both PointerFileEntries are null");
//        if (x is null || y is null)
//            return false;

//        return x.RelativeName == y.RelativeName &&
//               // x.Version.Equals(y.Version) && //DO NOT Compare on DateTime Version, we'd by inserting new PointerFileEntries just for the version
//               x.IsDeleted == y.IsDeleted &&
//               //x.CreationTimeUtc.Equals(y.CreationTimeUtc) &&
//               /*
//                   * Commenting out comparison on creationtime - this fails on docker on Synology (setting the value has no effect)
//                   *      Works on Windows
//                   *      Works on ubuntu (github runner)
//                   *      Doesn't work in docker / on synology
//                   */
//               x.LastWriteTimeUtc.Equals(y.LastWriteTimeUtc);
//    }

//    public int GetHashCode(PointerFileEntryDto obj)
//    {
//        return HashCode.Combine(obj.RelativeName,
//            //obj.Version,  //DO NOT Compare on DateTime Version
//            obj.IsDeleted,
//            //obj.CreationTimeUtc,
//            obj.LastWriteTimeUtc);
//    }
//}