using Arius.Core.New.UnitTests.Extensions;
using BenchmarkDotNet.Attributes;
using File = System.IO.File;

namespace Arius.Benchmarks;

[MemoryDiagnoser]
public class FileStreamBenchmark2
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

    /*
     * | Method                             | Mean         | Error       | StdDev      | Median       | Gen0      | Gen1      | Gen2      | Allocated    |
       |----------------------------------- |-------------:|------------:|------------:|-------------:|----------:|----------:|----------:|-------------:|
       | SmallFile_Read_DefaultBuffer       |     204.3 us |    16.27 us |    47.71 us |     190.1 us |    0.9766 |         - |         - |      5.97 KB |
       | SmallFile_Read_1024Buffer          |     187.1 us |     9.24 us |    26.07 us |     183.0 us |    0.9766 |         - |         - |      5.97 KB |
       | LargeFile_Read_Async_DefaultBuffer |  91,959.0 us | 3,402.95 us | 9,763.70 us |  88,945.3 us | 3833.3333 | 3833.3333 | 3833.3333 | 185691.24 KB |
       | LargeFile_Read_Async_32768Buffer   | 105,277.0 us | 2,286.01 us | 6,632.14 us | 104,569.6 us | 3800.0000 | 3800.0000 | 3800.0000 | 185691.25 KB |
     */
    private async Task<string> ReadAllBytesAsync(Stream stream)
    {
        using var memoryStream = new MemoryStream();
        await stream.CopyToAsync(memoryStream);
        return Convert.ToBase64String(memoryStream.ToArray()); // Converting to base64 to simulate some processing
    }


    [Benchmark]
    public async Task SmallFile_Read_DefaultBuffer()
    {
        using var stream = new FileStream(SmallFileName, FileMode.Open, FileAccess.Read, FileShare.Read);
        _ = await ReadAllBytesAsync(stream);
    }

    [Benchmark]
    public async Task SmallFile_Read_1024Buffer()
    {
        using var stream = new FileStream(SmallFileName, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1024);
        _ = await ReadAllBytesAsync(stream);
    }


    [Benchmark]
    public async Task LargeFile_Read_Async_DefaultBuffer()
    {
        using var stream = new FileStream(LargeFileName, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true);
        _ = await ReadAllBytesAsync(stream);
    }

    [Benchmark]
    public async Task LargeFile_Read_Async_32768Buffer()
    {
        using var stream = new FileStream(LargeFileName, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 32768, useAsync: true);
        _ = await ReadAllBytesAsync(stream);
    }

    


    [GlobalCleanup]
    public void Cleanup()
    {
        if (File.Exists(SmallFileName)) File.Delete(SmallFileName);
        if (File.Exists(LargeFileName)) File.Delete(LargeFileName);
    }
}