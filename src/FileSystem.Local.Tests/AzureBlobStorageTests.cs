using Azure.Storage.Blobs;
using Zio;
using Zio.FileSystems;
using ZioFileSystem.AzureBlobStorage;

namespace FileSystem.Local.Tests;

public class AzureBlobStorageTests
{
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
        var c = new ArchiveCommandHandler(bcc, "woutervr");

        IFileSystem lfs = new PhysicalFileSystem();
        var root = lfs.ConvertPathFromInternal(@"C:\Repos\Arius\LINQPad");
        lfs = new SubFileSystem(lfs, root, true);


        foreach (var f in lfs.EnumerateFiles(root))
        {
            var bf = new BinaryFile(lfs, f);
            var pf = (PointerFile)null;
            var fp = new FilePair(bf, pf);

            await c.UploadFileAsync(fp);
        }

        
    }
}


