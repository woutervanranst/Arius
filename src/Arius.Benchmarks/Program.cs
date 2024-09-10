using System.Diagnostics;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;

namespace Arius.Benchmarks;

internal static class Program
{
    private static void Main(string[] args)
    {
        //if (Debugger.IsAttached)
        //{
        //    // Direct call to the benchmark method for debugging purposes
        //    var benchmark = new FileStreamBenchmark();
        //    benchmark.Setup();
        //    benchmark.SmallFileSequentialScan(); // Or whichever benchmark method you want to step through
        //    benchmark.Cleanup();
        //}
        //else
        //{
        var config = ManualConfig.Create(DefaultConfig.Instance)
            //.WithOptions(ConfigOptions.DisableOptimizationsValidator);
            //.AddJob(Job.ShortRun)
            ;

            // Run the benchmarks only when not debugging
            var summary = BenchmarkRunner.Run<FileStreamBenchmark2>(config);
        //}
    }
}