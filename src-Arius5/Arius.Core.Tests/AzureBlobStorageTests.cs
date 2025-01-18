using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

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
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddUserSecrets<AzureBlobStorageTests>(optional: true)
            .AddEnvironmentVariables()
            .Build();

    }


}


