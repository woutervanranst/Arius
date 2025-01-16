using Azure.Storage.Blobs;
using System.Threading.Channels;
using Xunit.Abstractions;
using Zio;
using Zio.FileSystems;
using ZioFileSystem.AzureBlobStorage;

namespace FileSystem.Local.Tests;

public class AzureBlobStorageTests
{
    private readonly ITestOutputHelper _output;

    public AzureBlobStorageTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Delete()
    {
        var stateDatabaseFile = new FileInfo("state.db");
        stateDatabaseFile.Delete();
    }

    [Fact]
    public async Task Kak()
    {
        var cs = "DefaultEndpointsProtocol=https;AccountName=ariusci;AccountKey=;EndpointSuffix=core.windows.net";
        var bcc = new BlobContainerClient(cs, "atest", new BlobClientOptions());
        var handler = new ArchiveCommandHandler(bcc, "woutervr");

        IFileSystem lfs = new PhysicalFileSystem();
        //var root = lfs.ConvertPathFromInternal(@"C:\Repos\Arius\LINQPad");
        var root = lfs.ConvertPathFromInternal(@"C:\Users\RFC430\Downloads\");
        lfs = new SubFileSystem(lfs, root, true);

        var c = GetBoundedChannel<FilePair>(100, true);

        var parallelism = 0;

        var pt = Parallel.ForEachAsync(c.Reader.ReadAllAsync(),
            new ParallelOptions(),
            //GetParallelOptions(10),
            async (fp, ct) =>
            {
                Interlocked.Increment(ref parallelism);

                _output.WriteLine($"Parallelism {parallelism}");

                try
                {
                    if (fp.BinaryFile.Length > 1024 * 1024 * 10)
                        return;

                    _output.WriteLine($"Started {fp.BinaryFile.FullName}");
                    await handler.UploadFileAsync(fp);
                }
                catch (Exception e)
                {
                    _output.WriteLine(e.Message);
                }

                Interlocked.Decrement(ref parallelism);
            });

        foreach (var f in lfs.EnumerateFiles(UPath.Root, "*", SearchOption.AllDirectories))
        {
            if (f.IsPointerFile())
                continue;

            var bf = new BinaryFile(lfs, f);
            var pf = (PointerFile)null;
            var fp = new FilePair(bf, pf);

            await c.Writer.WriteAsync(fp);
        }

        c.Writer.Complete();

        await pt;


    }

    static Channel<T> GetBoundedChannel<T>(int capacity, bool singleWriter)
        => Channel.CreateBounded<T>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false,
            SingleWriter = singleWriter,
            SingleReader = false
        });

    ParallelOptions GetParallelOptions(int maxDegreeOfParallelism, CancellationToken cancellationToken = default)
        => new() { MaxDegreeOfParallelism = maxDegreeOfParallelism, CancellationToken = cancellationToken };
}


