using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
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

    //[Fact]
    //public async Task Hah()
    //{
    //    var afs = new AggregateFileSystem();

    //    var pfs = new PhysicalFileSystem();
    //    var sfs1 = new SubFileSystem(pfs, pfs.ConvertPathFromInternal("C:\\Users\\RFC430\\Downloads\\New folder"));
    //    var sfs2 = new SubFileSystem(pfs, pfs.ConvertPathFromInternal("C:\\Users\\RFC430\\OneDrive - Fluvius cvba\\Pictures\\Screenshots"));

    //    afs.AddFileSystem(sfs1);
    //    afs.AddFileSystem(sfs2);

    //    var x = afs.EnumerateFileEntries(UPath.Root).ToList();
    //}

    [Fact]
    public async Task Kak()
    {
        var cs = "DefaultEndpointsProtocol=https;AccountName=ariusci;AccountKey=;EndpointSuffix=core.windows.net";
        var bcc = new BlobContainerClient(cs, "atest", new BlobClientOptions());
        var handler = new ArchiveCommandHandler(bcc, "woutervr", AccessTier.Archive);

        IFileSystem pfs = new PhysicalFileSystem();
        //var root = lfs.ConvertPathFromInternal(@"C:\Repos\Arius\LINQPad");
        var root = pfs.ConvertPathFromInternal(@"C:\Users\RFC430\Downloads\New folder");
        var sfs = new SubFileSystem(pfs, root, true);
        var lfs = new FilePairFileSystem(sfs, true);
        //var lfs = FilePairFileSystem.From(pfs, root, true);

        var x = lfs.EnumerateFileEntries(UPath.Root, "*", SearchOption.AllDirectories).ToList();

        var c = GetBoundedChannel<FilePair>(100, true);

        var parallelism = 0;

        var pt = Parallel.ForEachAsync(c.Reader.ReadAllAsync(),
            //new ParallelOptions(),
            GetParallelOptions(1),
            async (fp, ct) =>
            {
                Interlocked.Increment(ref parallelism);

                _output.WriteLine($"Parallelism {parallelism}");

                try
                {
                    if (fp.BinaryFile.Exists && fp.BinaryFile.Length > 1024 * 1024 * 10)
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

        foreach (var fp in lfs.EnumerateFileEntries(UPath.Root, "*", SearchOption.AllDirectories))
        {
            await c.Writer.WriteAsync(FilePair.FromFileEntry(fp));
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


