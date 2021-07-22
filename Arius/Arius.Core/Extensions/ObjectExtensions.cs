namespace Arius.Core.Extensions
{
    internal static class ObjectExtensions
    { 
        public static T[] SingleToArray<T>(this T element)
        {
            return new T[] { element };
        }
    }
}
