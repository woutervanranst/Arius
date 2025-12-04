using Microsoft.Extensions.Logging;

namespace Arius.Core.Shared.Extensions;

internal static class IEnumerableExtensions
{
    public static IEnumerable<T> WithErrorLogging<T>(this IEnumerable<T> source, ILogger logger, string msg)
    {
        using var e = source.GetEnumerator();

        while (true)
        {
            bool moved;
            try
            {
                moved = e.MoveNext();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, msg);
                throw;
            }

            if (!moved) yield break;
            yield return e.Current;
        }
    }
}