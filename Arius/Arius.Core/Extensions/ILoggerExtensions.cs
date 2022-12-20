using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace Arius.Core.Extensions;

public static class ILoggerExtensions
{
    public static void LogError(this ILogger logger, Exception e)
    {
        logger.LogError(e, e.Message);
    }
}
