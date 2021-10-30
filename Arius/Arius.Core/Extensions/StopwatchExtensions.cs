using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Arius.Core.Extensions;

internal static class StopwatchExtensions
{
    public static double Time(this Stopwatch sw, Action action)
    {
        sw.Restart();
        sw.Start();

        action();

        sw.Stop();

        return Math.Round(sw.Elapsed.TotalSeconds, 3);
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="sw"></param>
    /// <param name="length">Length (in bytes)</param>
    /// <param name="action"></param>
    /// <returns></returns>
    public static (double MBps, double Mbps, double seconds) GetSpeed(this Stopwatch sw, long length, Action action)
    {
        sw.Restart();
        sw.Start();

        action();

        sw.Stop();

        var MBbs = Math.Round(length / (1024 * 1024 * sw.Elapsed.TotalSeconds), 3);
        var Mbps = Math.Round(length * 8 / (1024 * 1024 * sw.Elapsed.TotalSeconds), 3);
        var seconds = Math.Round(sw.Elapsed.TotalSeconds, 3);

        return (MBbs, Mbps, seconds);
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="sw"></param>
    /// <param name="length">Length (in bytes)</param>
    /// <param name="action"></param>
    /// <returns></returns>
    public async static Task<(double MBps, double Mbps, double seconds)> GetSpeedAsync(this Stopwatch sw, long length, Func<Task> action)
    {
        sw.Restart();
        sw.Start();

        await action();

        sw.Stop();

        var MBbs = Math.Round(length / (1024 * 1024 * sw.Elapsed.TotalSeconds), 3);
        var Mbps = Math.Round(length * 8 / (1024 * 1024 * sw.Elapsed.TotalSeconds), 3);
        var seconds = Math.Round(sw.Elapsed.TotalSeconds, 3);

        return (MBbs, Mbps, seconds);
    }

    public async static Task<(double MBps, double Mbps, double seconds, T1, T2, T3)> GetSpeedAsync<T1, T2, T3>(this Stopwatch sw, long length, Func<Task<(T1, T2, T3)>> action)
    {
        sw.Restart();
        sw.Start();

        var (t1, t2, t3) = await action();

        sw.Stop();

        var MBbs = Math.Round(length / (1024 * 1024 * sw.Elapsed.TotalSeconds), 3);
        var Mbps = Math.Round(length * 8 / (1024 * 1024 * sw.Elapsed.TotalSeconds), 3);
        var seconds = Math.Round(sw.Elapsed.TotalSeconds, 3);

        return (MBbs, Mbps, seconds, t1, t2, t3);
    }
}