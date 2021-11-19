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
            return new DirectoryInfo(stringValue);


            // Check whether the given path exists (throws FileNotFoundException) and is a File or Directory
            return File.GetAttributes(stringValue).HasFlag(FileAttributes.Directory) ? // as per https://stackoverflow.com/a/1395226/1582323
                new DirectoryInfo(stringValue) :
                new FileInfo(stringValue);

            //if (!File.GetAttributes(stringValue).HasFlag(FileAttributes.Directory) || // as per https://stackoverflow.com/a/1395226/1582323
            //    !Directory.Exists(stringValue))
            //    throw new NotSupportedException($"'{stringValue}' is not a valid directory");

            //return new DirectoryInfo(stringValue);

            //if (!Directory.Exists(PathString) || !File.GetAttributes(PathString).HasFlag(FileAttributes.Directory)) // as per https://stackoverflow.com/a/1395226/1582323
            //    return ValidationResult.Error($"'{PathString}' is not a valid directory");
        }
        throw new NotSupportedException("Can't convert value to directory.");
    }
}

public sealed class StringToAccessTierTypeConverter : TypeConverter
{
    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        return AccessTier.Cool;
        //if (value is string stringValue)
        //{
        //    if (!File.GetAttributes(stringValue).HasFlag(FileAttributes.Directory) || // as per https://stackoverflow.com/a/1395226/1582323
        //        !Directory.Exists(stringValue))
        //        throw new NotSupportedException($"'{stringValue}' is not a valid directory");

        //    return new DirectoryInfo(stringValue);

        //    //if (!Directory.Exists(PathString) || !File.GetAttributes(PathString).HasFlag(FileAttributes.Directory)) // as per https://stackoverflow.com/a/1395226/1582323
        //    //    return ValidationResult.Error($"'{PathString}' is not a valid directory");
        //}
        //throw new NotSupportedException("Can't convert value to directory.");
    }
}