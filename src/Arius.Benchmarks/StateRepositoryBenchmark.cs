using Arius.Core.Shared.Hashing;
using Arius.Core.Shared.StateRepositories;
using Arius.Core.Shared.Storage;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using Microsoft.Extensions.Logging.Abstractions;
using V51StateRepository = Arius.Core.Shared.StateRepositories.StateRepository;
using V5StateRepository = Arius.Benchmarks.StateRepositories.v50.StateRepository;

namespace Arius.Benchmarks;

[Config(typeof(StateRepositoryBenchmarkConfig))]
[MemoryDiagnoser]
[SimpleJob]
public class StateRepositoryBenchmark
{
    private V5StateRepository v50Repository = null!;
    private V51StateRepository v51Repository = null!;
    private StateRepositoryDbContextPool contextPool = null!;
    
    private string tempDir = null!;
    private FileInfo v50DatabaseFile = null!;
    private FileInfo v51DatabaseFile = null!;
    
    private Hash[] testHashes = null!;
    private PointerFileEntry[] pointerEntries = null!;
    private BinaryProperties[] binaryProperties = null!;

    //[Params(10, 100, 1000)]
    [Params(100)]
    public int ItemCount { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "AriusBenchmarks", Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        
        v50DatabaseFile = new FileInfo(Path.Combine(tempDir, "v50_state.db"));
        v51DatabaseFile = new FileInfo(Path.Combine(tempDir, "v51_state.db"));

        var logger = NullLogger<V5StateRepository>.Instance;
        var v51Logger = NullLogger<StateRepositoryDbContextPool>.Instance;
        
        v50Repository = new V5StateRepository(v50DatabaseFile, logger);

        contextPool = new StateRepositoryDbContextPool(v51DatabaseFile, true, v51Logger);
        v51Repository = new V51StateRepository(contextPool);

        GenerateTestData();
        SetupInitialData();
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        v50Repository?.Delete();
        contextPool?.Delete();
        
        if (Directory.Exists(tempDir))
        {
            Directory.Delete(tempDir, true);
        }
    }

    [IterationSetup]
    public void IterationSetup()
    {
        // Vacuum databases to ensure consistent starting state
        v50Repository.Vacuum();
        v51Repository.Vacuum();
    }

    private void GenerateTestData()
    {
        var random = new Random(12345); // Fixed seed for reproducibility
        testHashes = new Hash[ItemCount];
        pointerEntries = new PointerFileEntry[ItemCount];
        binaryProperties = new BinaryProperties[ItemCount];

        for (int i = 0; i < ItemCount; i++)
        {
            var hashBytes = new byte[32];
            random.NextBytes(hashBytes);
            var hash = Hash.FromBytes(hashBytes);
            testHashes[i] = hash;

            var relativeName = $"test/file_{i}.txt";
            var creationTime = DateTime.UtcNow.AddDays(-random.Next(1, 365));
            var lastWriteTime = creationTime.AddHours(random.Next(1, 24));

            pointerEntries[i] = new PointerFileEntry
            {
                Hash = hash,
                RelativeName = relativeName,
                CreationTimeUtc = creationTime,
                LastWriteTimeUtc = lastWriteTime
            };

            binaryProperties[i] = new BinaryProperties
            {
                Hash = hash,
                OriginalSize = random.Next(1024, 1024 * 1024),
                StorageTier = StorageTier.Hot
            };
        }
    }

    private void SetupInitialData()
    {
        // Add binary properties to both repositories for GetBinaryProperty benchmarks
        v50Repository.AddBinaryProperties(binaryProperties);
        v51Repository.AddBinaryProperties(binaryProperties);
    }

    //   Expected Performance Insights:
    // The benchmark should reveal significant performance improvements in v5.1 implementation due to:
    // 1. Bulk operations (BulkInsertOrUpdate) vs individual EF operations
    // 2. Compiled queries vs dynamic LINQ queries
    // 3. Context pooling vs manual context creation per operation
    // 
    // Results:
    // | Method                            | ItemCount | Mean      | Error     | StdDev    | Allocated  |
    // |---------------------------------- |---------- |----------:|----------:|----------:|-----------:|
    // | V50_UpsertPointerFileEntries      | 100       | 19.628 ms | 2.6592 ms | 7.7990 ms | 1971.56 KB |
    // | V51_UpsertPointerFileEntries      | 100       |  5.388 ms | 0.2322 ms | 0.6736 ms |  158.64 KB |
    // | V50_GetBinaryProperty_Existing    | 100       |  2.056 ms | 0.0631 ms | 0.1842 ms |   55.29 KB |
    // | V51_GetBinaryProperty_Existing    | 100       |  1.565 ms | 0.0466 ms | 0.1346 ms |   11.02 KB |
    // | V50_GetBinaryProperty_NonExisting | 100       |  2.050 ms | 0.0552 ms | 0.1584 ms |   53.71 KB |
    // | V51_GetBinaryProperty_NonExisting | 100       |  1.547 ms | 0.0500 ms | 0.1442 ms |   10.63 KB |
    // | V50_GetBinaryProperty_Batch       | 100       |  5.256 ms | 0.1728 ms | 0.4818 ms |  483.73 KB |
    // | V51_GetBinaryProperty_Batch       | 100       |  1.917 ms | 0.0848 ms | 0.2419 ms |   74.16 KB |

    [Benchmark]
    public void V50_UpsertPointerFileEntries()
    {
        v50Repository.UpsertPointerFileEntries(pointerEntries);
    }

    [Benchmark]
    public void V51_UpsertPointerFileEntries()
    {
        v51Repository.UpsertPointerFileEntries(pointerEntries);
    }

    [Benchmark]
    public object? V50_GetBinaryProperty_Existing()
    {
        var hash = testHashes[0]; // First hash should exist
        return v50Repository.GetBinaryProperty(hash);
    }

    [Benchmark]
    public object? V51_GetBinaryProperty_Existing()
    {
        var hash = testHashes[0]; // First hash should exist
        return v51Repository.GetBinaryProperty(hash);
    }

    [Benchmark]
    public object? V50_GetBinaryProperty_NonExisting()
    {
        var nonExistentHash = Hash.FromBytes(new byte[32]); // All zeros, should not exist
        return v50Repository.GetBinaryProperty(nonExistentHash);
    }

    [Benchmark]
    public object? V51_GetBinaryProperty_NonExisting()
    {
        var nonExistentHash = Hash.FromBytes(new byte[32]); // All zeros, should not exist
        return v51Repository.GetBinaryProperty(nonExistentHash);
    }

    [Benchmark]
    public void V50_GetBinaryProperty_Batch()
    {
        for (int i = 0; i < Math.Min(10, ItemCount); i++)
        {
            v50Repository.GetBinaryProperty(testHashes[i]);
        }
    }

    [Benchmark]
    public void V51_GetBinaryProperty_Batch()
    {
        for (int i = 0; i < Math.Min(10, ItemCount); i++)
        {
            v51Repository.GetBinaryProperty(testHashes[i]);
        }
    }
}

public class StateRepositoryBenchmarkConfig : ManualConfig
{
    public StateRepositoryBenchmarkConfig()
    {
        AddDiagnoser(MemoryDiagnoser.Default);
    }
}