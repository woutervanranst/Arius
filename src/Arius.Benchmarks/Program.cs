using Arius.Core.Domain.Storage.FileSystem;
using Arius.Core.New.UnitTests.Fixtures;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;
using WouterVanRanst.Utils.Extensions;

namespace HasherBenchmark;

[MemoryDiagnoser]
public class HashingBenchmarks
{
    private const string Salt = "woutervanranst";
    private readonly string smallFile;
    private readonly string largeFile;
    private readonly Arius.Core.Services.SHA256Hasher arius3Hasher;
    private readonly Arius.Core.Infrastructure.SHA256Hasher arius4Hasher;

    public HashingBenchmarks()
    {
        // Setup files
        smallFile = "smallTestFile.bin";
        largeFile = "largeTestFile.bin";

        // Create test files
        FileUtils.CreateRandomFile(smallFile, 1024 * 100); // 100 KB
        FileUtils.CreateRandomFile(largeFile, 1024 * 1024 * 100); // 100 MB

        // Initialize hashers
        arius3Hasher = new Arius.Core.Services.SHA256Hasher(Salt);
        arius4Hasher = new Arius.Core.Infrastructure.SHA256Hasher(Salt);
    }

    [Benchmark]
    public string HashSmallFileArius3()
    {
        return arius3Hasher.GetBinaryHash(smallFile).Value.BytesToHexString();
    }

    [Benchmark]
    public async Task<string> HashSmallFileArius4()
    {
        return (await arius4Hasher.GetHashAsync(new BinaryFile(null, smallFile))).Value.BytesToHexString();
    }

    [Benchmark]
    public string HashLargeFileArius3()
    {
        return arius3Hasher.GetBinaryHash(largeFile).Value.BytesToHexString();
    }

    [Benchmark]
    public async Task<string> HashLargeFileArius4()
    {
        return (await arius4Hasher.GetHashAsync(new BinaryFile(null, largeFile))).Value.BytesToHexString();
    }
}

internal static class Program
{
    private static void Main(string[] args)
    {
        //var config = ManualConfig.Create(DefaultConfig.Instance)
        //    .WithOptions(ConfigOptions.DisableOptimizationsValidator);

        var summary = BenchmarkRunner.Run<HashingBenchmarks>(/*config*/);
    }
}
