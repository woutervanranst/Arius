using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Azure.Storage.Blobs.Models;

namespace Arius.SpectreCli.Utils;

public sealed class DirectoryTypeConverter : TypeConverter
{
    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        if (value is string stringValue)
        {
            if (!File.GetAttributes(stringValue).HasFlag(FileAttributes.Directory) || // as per https://stackoverflow.com/a/1395226/1582323
                !Directory.Exists(stringValue))
                throw new NotSupportedException($"'{stringValue}' is not a valid directory");

            return new DirectoryInfo(stringValue);

            //if (!Directory.Exists(PathString) || !File.GetAttributes(PathString).HasFlag(FileAttributes.Directory)) // as per https://stackoverflow.com/a/1395226/1582323
            //    return ValidationResult.Error($"'{PathString}' is not a valid directory");
        }
        throw new NotSupportedException("Can't convert value to directory.");
    }
}

public sealed class TierTypeConverter : TypeConverter
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