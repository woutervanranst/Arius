namespace Arius.Core.Domain.Extensions;

public static class PathExtensions
{
    public static string ToPlatformNeutralPath(this string platformSpecificPath)
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

    public static string ToPlatformSpecificPath(this string platformNeutralPath)
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

    internal const char PLATFORM_NEUTRAL_DIRECTORY_SEPARATOR_CHAR = '/';
}