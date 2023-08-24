using System;
using System.IO;
using Arius.Core.Models;
using Arius.Core.Repositories.StateDb;

namespace Arius.Core.Extensions;

internal static class PointerFileEntryConverter
{
    /// <summary>
    /// Deconstructs the given relativeName into the platform SPECIFIC path components
    /// </summary>
    public static (string RelativeParentPath, string DirectoryName, string Name) Deconstruct(string relativeName)
    {
        var relativePath       = Path.GetDirectoryName(relativeName);
        var lastSepIndex       = relativePath.LastIndexOf(Path.DirectorySeparatorChar);
        var directoryName      = relativePath[(lastSepIndex + 1)..];
        var relativeParentPath = lastSepIndex == -1 ? "" : relativePath[..lastSepIndex];
        var name               = Path.GetFileName(relativeName);

        return (relativeParentPath, directoryName, name);
    }

    public static PointerFileEntryDto ToPointerFileEntryDto(this PointerFileEntry pfe)
    {
        if (pfe.Chunk is not null)
        {
            throw new NotImplementedException(); // not sure what to do here
        }

        return new PointerFileEntryDto
        {
            BinaryHash         = pfe.BinaryHash.Value, // convert the bytes
            RelativeParentPath = ToPlatformNeutralPath(pfe.RelativeParentPath), // convert to platform neutral
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
            RelativeParentPath = ToPlatformSpecificPath(pfeDto.RelativeParentPath),
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

    public static string ToPlatformNeutralPath(string platformSpecificPath)
    {
        if (Path.DirectorySeparatorChar == PLATFORM_NEUTRAL_DIRECTORY_SEPARATOR_CHAR)
            return platformSpecificPath;

        return platformSpecificPath.Replace(Path.DirectorySeparatorChar, PLATFORM_NEUTRAL_DIRECTORY_SEPARATOR_CHAR);

        //if (platformSpecific is null)
        //    return null;
        //if (Path.DirectorySeparatorChar == PLATFORM_NEUTRAL_DIRECTORY_SEPARATOR_CHAR)
        //    return platformSpecific;
        //return platformSpecific with { RelativeName = platformSpecific.RelativeName.Replace(Path.DirectorySeparatorChar, PLATFORM_NEUTRAL_DIRECTORY_SEPARATOR_CHAR) };
    }

    public static string ToPlatformSpecificPath(string platformNeutralPath)
    {
        // TODO UNIT TEST for linux pointers (already done if run in the github runner?

        if (Path.DirectorySeparatorChar == PLATFORM_NEUTRAL_DIRECTORY_SEPARATOR_CHAR)
            return platformNeutralPath;

        return platformNeutralPath.Replace(PLATFORM_NEUTRAL_DIRECTORY_SEPARATOR_CHAR, Path.DirectorySeparatorChar);

        //if (platformNeutral is null)
        //    return null;
        //if (Path.DirectorySeparatorChar == PLATFORM_NEUTRAL_DIRECTORY_SEPARATOR_CHAR)
        //    return platformNeutral;
        //return platformNeutral with { RelativeName = platformNeutral.RelativeName.Replace(PLATFORM_NEUTRAL_DIRECTORY_SEPARATOR_CHAR, Path.DirectorySeparatorChar) };
    }
}