using System;

namespace Arius.CliSpectre.Utils;

/// <summary>
/// Marks a field as needed to be obfuscated in logs
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false, AllowMultiple = false)]
sealed class ObfuscateInLogAttribute : Attribute
{
    // See the attribute guidelines at 
    //  http://go.microsoft.com/fwlink/?LinkId=85236
}