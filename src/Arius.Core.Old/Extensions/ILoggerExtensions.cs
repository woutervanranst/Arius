using System;
using Microsoft.Extensions.Logging;

namespace Arius.Core.Extensions;

public static class ILoggerExtensions
{
    public static void LogError(this ILogger logger, Exception e)
    {
        logger.LogError(e, e.Message);
    }
}
