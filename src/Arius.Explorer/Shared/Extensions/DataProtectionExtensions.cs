using System.Security.Cryptography;
using System.Text;

namespace Arius.Explorer.Shared.Extensions;

public static class DataProtectionExtensions
{
    public static string Protect(this string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
            return plainText;

        try
        {
            var data = Encoding.UTF8.GetBytes(plainText);
            var protectedData = ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(protectedData);
        }
        catch (Exception)
        {
            // If protection fails, return the original value
            // This could happen on non-Windows systems or if DPAPI is not available
            return plainText;
        }
    }

    public static string Unprotect(this string protectedText)
    {
        if (string.IsNullOrEmpty(protectedText))
            return protectedText;

        try
        {
            // First check if it's valid Base64 before attempting to unprotect
            var protectedData = Convert.FromBase64String(protectedText);
            var data = ProtectedData.Unprotect(protectedData, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(data);
        }
        catch (FormatException)
        {
            // Not valid Base64, likely plain text from before encryption was added
            return protectedText;
        }
        catch (Exception)
        {
            // If unprotection fails for other reasons, return the original value
            // This could happen if the data is corrupted or can't be decrypted
            return protectedText;
        }
    }
}