using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using Azure.Storage.Blobs.Models;

namespace Arius.CliSpectre.Utils;

public sealed class StringToFileSystemInfoTypeConverter : TypeConverter
{
    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        if (value is string stringValue)
        {
            // Check whether the given path exists (throws FileNotFoundException) and is a File or Directory
            return File.GetAttributes(stringValue).HasFlag(FileAttributes.Directory) ? // as per https://stackoverflow.com/a/1395226/1582323
                new DirectoryInfo(stringValue) :
                new FileInfo(stringValue);
        }
        throw new NotSupportedException($"{value} is not a valid file or directory");
    }
}

public sealed class StringToDirectoryInfoTypeConverter : TypeConverter
{
    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        if (value is string stringValue)
        {
            // Check whether the given path exists (throws FileNotFoundException) and is a File or Directory
            FileSystemInfo r = File.GetAttributes(stringValue).HasFlag(FileAttributes.Directory) ? // as per https://stackoverflow.com/a/1395226/1582323
                new DirectoryInfo(stringValue) :
                new FileInfo(stringValue);

            if (r is DirectoryInfo)
                return r;
        }
        throw new ArgumentException($"{value} is not a valid directory");
    }
}

public sealed class StringToAccessTierTypeConverter : TypeConverter
{
    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        var r = (AccessTier)(string)value;
        return r;
    }
}
