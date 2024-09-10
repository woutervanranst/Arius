using Arius.Core.New.UnitTests.Extensions;
using BenchmarkDotNet.Attributes;
using File = System.IO.File;

namespace Arius.Benchmarks;

[MemoryDiagnoser]
public class FileStreamBenchmark
{
    private const string SmallFileName = "smallfile.dat";
    private const string LargeFileName = "largefile.dat";
    private const int    SmallFileSize = 1000;
    private const int    LargeFileSize = 32 * 1024 * 1024; // 32 MB

    private readonly Arius.Core.Infrastructure.Services.SHA256Hasher hasher = new("woutervanranst");

    [GlobalSetup]
    public void Setup()
    {
        FileUtils.CreateRandomFile(SmallFileName, SmallFileSize);
        FileUtils.CreateRandomFile(LargeFileName, LargeFileSize);
    }

    private async Task<string> ReadAllBytesAsync(Stream stream)
    {
        using var memoryStream = new MemoryStream();
        await stream.CopyToAsync(memoryStream);
        return Convert.ToBase64String(memoryStream.ToArray()); // Converting to base64 to simulate some processing
    }


    // SMALL FILE (1000 bytes) Benchmarks


    /* w/ FileShare.None

     * | Method                                             | Mean     | Error    | StdDev    | Median   | Gen0   | Allocated |
       |--------------------------------------------------- |---------:|---------:|----------:|---------:|-------:|----------:|
       | SmallFile_Read_1024Buffer                          | 280.8 us | 30.69 us |  90.49 us | 309.0 us | 0.9766 |      6 KB |
       | SmallFile_Read_SequentialScan_1024Buffer           | 291.5 us | 28.02 us |  82.62 us | 316.3 us | 0.9766 |   5.98 KB |
       | SmallFile_Read_NoAsync_1024Buffer                  | 262.0 us | 33.07 us |  97.51 us | 289.8 us | 0.9766 |   5.91 KB |
       | SmallFile_Read_Async_1024Buffer                    | 356.2 us | 38.53 us | 113.60 us | 394.8 us | 0.9766 |   6.29 KB |
       | SmallFile_Read_Async_1024Buffer_2                  | 316.5 us | 39.46 us | 116.34 us | 301.9 us | 0.9766 |   6.32 KB |
       | SmallFile_Read_SequentialScan_WithAsync_1024Buffer | 327.4 us | 35.55 us | 104.27 us | 294.1 us | 0.9766 |   6.28 KB |

        w/ FileShare.Read
       | Method                                             | Mean      | Error    | StdDev    | Median    | Gen0   | Allocated |
       |--------------------------------------------------- |----------:|---------:|----------:|----------:|-------:|----------:|
       | SmallFile_Read_1024Buffer                          |  90.59 us | 2.665 us |  7.474 us |  87.38 us | 0.9766 |   6.01 KB |
       | SmallFile_Read_SequentialScan_1024Buffer           |  83.63 us | 1.657 us |  3.566 us |  83.05 us | 0.9766 |   6.01 KB |
       | SmallFile_Read_NoAsync_1024Buffer                  |  82.89 us | 1.604 us |  3.749 us |  82.11 us | 0.9766 |   6.01 KB |
       | SmallFile_Read_Async_1024Buffer                    | 142.65 us | 2.802 us |  2.752 us | 143.66 us | 0.9766 |   6.31 KB |
       | SmallFile_Read_Async_1024Buffer_2                  | 189.24 us | 9.733 us | 27.293 us | 183.89 us | 0.9766 |   6.31 KB |
       | SmallFile_Read_SequentialScan_WithAsync_1024Buffer | 194.92 us | 7.453 us | 21.504 us | 188.17 us | 0.9766 |   6.31 KB |
     */

    [Benchmark]
    public async Task SmallFile_Read_1024Buffer()
    {
        using var stream = new FileStream(SmallFileName, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1024);
        _ = await ReadAllBytesAsync(stream);
    }

    [Benchmark]
    public async Task SmallFile_Read_SequentialScan_1024Buffer()
    {
        using var stream = new FileStream(SmallFileName, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1024, FileOptions.SequentialScan);
        _ = await ReadAllBytesAsync(stream);
    }

    [Benchmark]
    public async Task SmallFile_Read_NoAsync_1024Buffer()
    {
        using var stream = new FileStream(SmallFileName, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1024, useAsync: false);
        _ = await ReadAllBytesAsync(stream);
    }

    [Benchmark]
    public async Task SmallFile_Read_Async_1024Buffer()
    {
        using var stream = new FileStream(SmallFileName, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1024, useAsync: true);
        _ = await ReadAllBytesAsync(stream);
    }

    [Benchmark]
    public async Task SmallFile_Read_Async_1024Buffer_2()
    {
        using var stream = new FileStream(SmallFileName, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1024, FileOptions.Asynchronous);
        _ = await ReadAllBytesAsync(stream);
    }

    [Benchmark]
    public async Task SmallFile_Read_SequentialScan_WithAsync_1024Buffer()
    {
        using var stream = new FileStream(SmallFileName, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1024, FileOptions.SequentialScan | FileOptions.Asynchronous);
        _ = await ReadAllBytesAsync(stream);
    }




    // LARGE FILE (32 MB) Benchmarks

    /*
     * | Method                                              | Mean      | Error     | StdDev    | Median   | Gen0      | Gen1      | Gen2      | Allocated |
       |---------------------------------------------------- |----------:|----------:|----------:|---------:|----------:|----------:|----------:|----------:|
       | LargeFile_Read_SequentialScan_32768Buffer           | 102.60 ms | 10.923 ms | 32.206 ms | 88.05 ms | 3750.0000 | 3750.0000 | 3750.0000 | 181.34 MB |
       | LargeFile_Read_NoAsync_32768Buffer                  |  65.80 ms |  1.308 ms |  3.183 ms | 64.72 ms | 3857.1429 | 3857.1429 | 3857.1429 | 181.34 MB |
       | LargeFile_Read_Async_32768Buffer                    |  64.58 ms |  2.589 ms |  7.633 ms | 66.14 ms | 3875.0000 | 3875.0000 | 3875.0000 | 181.34 MB |
       | LargeFile_Read_Async_32768Buffer_2                  |  70.17 ms |  1.342 ms |  1.597 ms | 70.22 ms | 3857.1429 | 3857.1429 | 3857.1429 | 181.34 MB |
       | LargeFile_Read_SequentialScan_WithAsync_32768Buffer |  72.86 ms |  1.433 ms |  3.115 ms | 72.01 ms | 3857.1429 | 3857.1429 | 3857.1429 | 181.34 MB |
     */

    [Benchmark]
    public async Task LargeFile_Read_SequentialScan_32768Buffer()
    {
        using var stream = new FileStream(LargeFileName, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 32768, FileOptions.SequentialScan);
        _ = await ReadAllBytesAsync(stream);
    }

    [Benchmark]
    public async Task LargeFile_Read_NoAsync_32768Buffer()
    {
        using var stream = new FileStream(LargeFileName, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 32768, useAsync: false);
        _ = await ReadAllBytesAsync(stream);
    }

    [Benchmark]
    public async Task LargeFile_Read_Async_32768Buffer()
    {
        using var stream = new FileStream(LargeFileName, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 32768, useAsync: true);
        _ = await ReadAllBytesAsync(stream);
    }

    [Benchmark]
    public async Task LargeFile_Read_Async_32768Buffer_2()
    {
        using var stream = new FileStream(LargeFileName, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 32768, FileOptions.Asynchronous);
        _ = await ReadAllBytesAsync(stream);
    }

    [Benchmark]
    public async Task LargeFile_Read_SequentialScan_WithAsync_32768Buffer()
    {
        using var stream = new FileStream(LargeFileName, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 32768, FileOptions.SequentialScan | FileOptions.Asynchronous);
        _ = await ReadAllBytesAsync(stream);
    }



    [GlobalCleanup]
    public void Cleanup()
    {
        if (File.Exists(SmallFileName)) File.Delete(SmallFileName);
        if (File.Exists(LargeFileName)) File.Delete(LargeFileName);
    }
}