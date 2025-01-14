using System;

namespace FileSystem.Local;

internal static class PathExtensions
{
    internal const char PLATFORM_NEUTRAL_DIRECTORY_SEPARATOR_CHAR = '/';

    public static string ToPlatformNeutralPath(this string platformSpecificPath)
    {
        if (string.IsNullOrWhiteSpace(platformSpecificPath))
            throw new ArgumentException("Path cannot be null or whitespace.", nameof(platformSpecificPath));

        // Normalize all separators to platform-neutral
        var normalizedPath = platformSpecificPath
            .Replace('\\', PLATFORM_NEUTRAL_DIRECTORY_SEPARATOR_CHAR)
            .Replace(SIO.Path.DirectorySeparatorChar, PLATFORM_NEUTRAL_DIRECTORY_SEPARATOR_CHAR);

        // Collapse multiple consecutive separators
        return CollapseRedundantSeparators(normalizedPath, PLATFORM_NEUTRAL_DIRECTORY_SEPARATOR_CHAR);
    }

    public static string ToPlatformSpecificPath(this string platformNeutralPath)
    {
        if (string.IsNullOrWhiteSpace(platformNeutralPath))
            throw new ArgumentException("Path cannot be null or whitespace.", nameof(platformNeutralPath));

        // Normalize all separators to platform-specific
        var normalizedPath = platformNeutralPath
            .Replace('/', SIO.Path.DirectorySeparatorChar)
            .Replace('\\', SIO.Path.DirectorySeparatorChar);

        // Collapse multiple consecutive separators
        return CollapseRedundantSeparators(normalizedPath, SIO.Path.DirectorySeparatorChar);
    }

    private static string CollapseRedundantSeparators(string path, char separator)
    {
        // Replace multiple consecutive separators with a single one
        while (path.Contains($"{separator}{separator}"))
        {
            path = path.Replace($"{separator}{separator}", $"{separator}");
        }
        return path;
    }
}