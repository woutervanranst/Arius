﻿using System;

namespace Arius.Core.Extensions;

internal static class LongExtensions
{
    // https://stackoverflow.com/a/11124118/1582323

    public static string GetBytesReadable(this long i)
    {
        // Get absolute value
        long absolute_i = i < 0 ? -i : i;
        // Determine the suffix and readable value
        string suffix;
        double readable;
        if (absolute_i >= 0x1000000000000000) // Exabyte
        {
            suffix = "EB";
            readable = i >> 50;
        }
        else if (absolute_i >= 0x4000000000000) // Petabyte
        {
            suffix = "PB";
            readable = i >> 40;
        }
        else if (absolute_i >= 0x10000000000) // Terabyte
        {
            suffix = "TB";
            readable = i >> 30;
        }
        else if (absolute_i >= 0x40000000) // Gigabyte
        {
            suffix = "GB";
            readable = i >> 20;
        }
        else if (absolute_i >= 0x100000) // Megabyte
        {
            suffix = "MB";
            readable = i >> 10;
        }
        else if (absolute_i >= 0x400) // Kilobyte
        {
            suffix = "KB";
            readable = i;
        }
        else
        {
            return i.ToString("0 B"); // Byte
        }
        // Divide by 1024 to get fractional value
        readable /= 1024;
        // Return formatted number with suffix
        return readable.ToString("0.### ") + suffix;
    }


    public enum Size
    {
        KB
    }
    public static string GetBytesReadable(this long i, Size size)
    {
        if (size == Size.KB)
            return $"{i / 1024:N0} {size:g}";

        throw new NotImplementedException();
    }
}