using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;

namespace HasherBenchmark;

internal static class Program
{
    private static void Main(string[] args)
    {
        //var config = ManualConfig.Create(DefaultConfig.Instance)
        //    .WithOptions(ConfigOptions.DisableOptimizationsValidator);

        var summary = BenchmarkRunner.Run<Sha256HasherBenchmarks>(/*config*/);
    }
}
