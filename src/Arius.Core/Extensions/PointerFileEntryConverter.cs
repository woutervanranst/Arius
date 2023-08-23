using System;
using System.IO;
using Arius.Core.Models;
using Arius.Core.Repositories.StateDb;

namespace Arius.Core.Extensions;

internal static class PointerFileEntryConverter
{
    public static PointerFileEntryDto ToPointerFileEntryDto(this PointerFileEntry pfe)
    {

        return new PointerFileEntryDto
        {
            BinaryHash         = pfe.BinaryHash.Value, // convert the bytes
            RelativeParentPath = ToPlatformNeutral(pfe.RelativeParentPath), // convert to platform neutral
            DirectoryName      = pfe.DirectoryName,
            Name               = pfe.Name,
            VersionUtc         = pfe.VersionUtc,
            IsDeleted          = pfe.IsDeleted,
            CreationTimeUtc    = pfe.CreationTimeUtc,
            LastWriteTimeUtc   = pfe.LastWriteTimeUtc
        };
    }

    public static PointerFileEntry ToPointerFileEntry(this PointerFileEntryDto pfeDto)
    {
        return new PointerFileEntry
        {
            BinaryHash         = new BinaryHash(pfeDto.BinaryHash),
            RelativeParentPath = ToPlatformSpecific(pfeDto.RelativeParentPath),
            DirectoryName      = pfeDto.DirectoryName,
            Name               = pfeDto.Name,
            VersionUtc         = pfeDto.VersionUtc,
            IsDeleted          = pfeDto.IsDeleted,
            CreationTimeUtc    = pfeDto.CreationTimeUtc,
            LastWriteTimeUtc   = pfeDto.LastWriteTimeUtc,
            Chunk              = pfeDto.Chunk
        };
    }

    private const char PLATFORM_NEUTRAL_DIRECTORY_SEPARATOR_CHAR = '/';

    private static string ToPlatformNeutral(string platformSpecific)
    {
        if (Path.DirectorySeparatorChar == PLATFORM_NEUTRAL_DIRECTORY_SEPARATOR_CHAR)
            return platformSpecific;

        return platformSpecific.Replace(Path.DirectorySeparatorChar, PLATFORM_NEUTRAL_DIRECTORY_SEPARATOR_CHAR);

        //if (platformSpecific is null)
        //    return null;
        //if (Path.DirectorySeparatorChar == PLATFORM_NEUTRAL_DIRECTORY_SEPARATOR_CHAR)
        //    return platformSpecific;
        //return platformSpecific with { RelativeName = platformSpecific.RelativeName.Replace(Path.DirectorySeparatorChar, PLATFORM_NEUTRAL_DIRECTORY_SEPARATOR_CHAR) };
    }

    private static string ToPlatformSpecific(string platformNeutral)
    {
        // TODO UNIT TEST for linux pointers (already done if run in the github runner?

        if (Path.DirectorySeparatorChar == PLATFORM_NEUTRAL_DIRECTORY_SEPARATOR_CHAR)
            return platformNeutral;

        return platformNeutral.Replace(PLATFORM_NEUTRAL_DIRECTORY_SEPARATOR_CHAR, Path.DirectorySeparatorChar);

        //if (platformNeutral is null)
        //    return null;
        //if (Path.DirectorySeparatorChar == PLATFORM_NEUTRAL_DIRECTORY_SEPARATOR_CHAR)
        //    return platformNeutral;
        //return platformNeutral with { RelativeName = platformNeutral.RelativeName.Replace(PLATFORM_NEUTRAL_DIRECTORY_SEPARATOR_CHAR, Path.DirectorySeparatorChar) };
    }
}