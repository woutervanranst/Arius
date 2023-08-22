using System;
using Microsoft.Extensions.Logging;

namespace Arius.Cli.Utils;

/// <summary>
/// Marks a field as needed to be obfuscated in logs
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false, AllowMultiple = false)]
sealed class ObfuscateInLogAttribute : Attribute
{
    // See the attribute guidelines at 
    //  http://go.microsoft.com/fwlink/?LinkId=85236
}

public static class LoggingExtensions
{
    public static void LogProperties(this ILogger logger, object options)
    {
        foreach (var property in options.GetType().GetProperties())
        {
            var value = property.GetValue(options)?.ToString();
            if (Attribute.IsDefined(property, typeof(ObfuscateInLogAttribute)))
                value = "***";

            logger.LogDebug($"{property.Name}: {value}");
        }
    }
}