using System.Text.Json.Serialization;
using Arius.Core.Commands;
using Arius.Core.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
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

        var config = configuration.GetSection("RepositoryOptions").Get<TestRemoteRepositoryOptions>();

        var c = new ArchiveCommand()
        {
            AccountName   = config.AccountName,
            AccountKey    = config.AccountKey,
            ContainerName = config.ContainerName ?? "atest",
            Passphrase    = config.Passphrase,
            RemoveLocal   = false,
            Tier          = StorageTier.Cool,
            LocalRoot     = new DirectoryInfo("C:\\Users\\RFC430\\Downloads\\New folder")
        };

        var ch = new ArchiveCommandHandler(NullLogger<ArchiveCommandHandler>.Instance);
        await ch.Handle(c, CancellationToken.None);

    }

    public record TestRemoteRepositoryOptions
    {
        public string AccountName { get; init; }

        public string AccountKey { get; init; }

        //[JsonIgnore]
        public string ContainerName { get; set; }

        public string Passphrase { get; init; }
    }

}


