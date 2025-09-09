using Arius.Core.Shared.FileSystem;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Zio;
using Zio.FileSystems;

namespace Arius.Core.Tests.Helpers.Fixtures;

public class Fixture : IDisposable
{
    public const string PASSPHRASE = "wouterpassphrase";

    public TestRemoteRepositoryOptions? RepositoryOptions   { get; } // can be null when no appsettings etc
    public IOptions<AriusConfiguration> AriusConfiguration  { get; }
    public IFileSystem                  FileSystem          { get; }
    public DirectoryInfo                TestRunSourceFolder { get; }

    public Fixture()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddUserSecrets<Fixture>(optional: true)
            .AddEnvironmentVariables()
            .Build();

        RepositoryOptions = configuration.GetSection("RepositoryOptions").Get<TestRemoteRepositoryOptions>();

        var ariusConfig = new AriusConfiguration();
        configuration.Bind(ariusConfig);
        AriusConfiguration = Options.Create(ariusConfig);


        TestRunSourceFolder = Directory.CreateTempSubdirectory($"Arius.Core.Tests-{DateTime.Now:yyyyMMddTHHmmss}_{Guid.CreateVersion7()}");
        TestRunSourceFolder.Create();

        var pfs = new PhysicalFileSystem();
        var sfs = new SubFileSystem(pfs, pfs.ConvertPathFromInternal(TestRunSourceFolder.FullName));
        FileSystem = new FilePairFileSystem(sfs);
    }

    public void Dispose()
    {
        FileSystem.Dispose();

        // Temp folder cleanup is handled by Utils.CleanupLocalTemp
        //if (TestRunSourceFolder.Exists)
        //{
        //    TestRunSourceFolder.Delete(recursive: true);
        //}
    }
}

public record TestRemoteRepositoryOptions
{
    public required string AccountName   { get; init; }
    public required string AccountKey    { get; init; }
    public required string ContainerName { get; set; }
    public          string Passphrase    => Fixture.PASSPHRASE;
}