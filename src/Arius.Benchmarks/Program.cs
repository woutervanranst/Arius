using BenchmarkDotNet.Running;

namespace Arius.Benchmarks;

internal class Program
{
    static void Main(string[] args)
    {
        BenchmarkRunner.Run<StateRepositoryBenchmark>();
    }
}