using System;

namespace Arius.Core.Extensions
{
    internal static class StringExtensions
    {
        // https://stackoverflow.com/questions/4101539/c-sharp-removing-substring-from-end-of-string


        public static string TrimEnd(this string inputText, string value, StringComparison comparisonType = StringComparison.CurrentCultureIgnoreCase)
        {
            if (!string.IsNullOrEmpty(value))
            {
                while (!string.IsNullOrEmpty(inputText) && inputText.EndsWith(value, comparisonType))
                {
                    inputText = inputText.Substring(0, inputText.Length - value.Length);
                }
            }

            return inputText;
        }
    }
}
