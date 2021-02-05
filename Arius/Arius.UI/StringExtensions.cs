using System;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace Arius.UI
{
    internal static class StringExtensions
    {
        public static string Protect(this string value)
        {
            if (string.IsNullOrEmpty(value))
                return value;

            byte[] entropy = Encoding.UTF8.GetBytes(Assembly.GetExecutingAssembly().FullName);
            byte[] data = Encoding.UTF8.GetBytes(value);
            string protectedData = Convert.ToBase64String(ProtectedData.Protect(data, entropy, DataProtectionScope.CurrentUser));
            return protectedData;
        }

        public static string Unprotect(this string value)
        {
            if (string.IsNullOrEmpty(value))
                return value;

            byte[] protectedData = Convert.FromBase64String(value);
            byte[] entropy = Encoding.UTF8.GetBytes(Assembly.GetExecutingAssembly().FullName);
            string data = Encoding.UTF8.GetString(ProtectedData.Unprotect(protectedData, entropy, DataProtectionScope.CurrentUser));
            return data;
        }
    }
}
