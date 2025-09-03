using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Zio;

namespace Arius.Core.Tests.Helpers.Fixtures;

public abstract class FixtureBase : IDisposable
{
    public TestRemoteRepositoryOptions RepositoryOptions { get; }
    public IOptions<AriusConfiguration> AriusConfiguration { get; }
    public abstract IFileSystem FileSystem { get; }

    protected FixtureBase()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddUserSecrets<FixtureBase>(optional: true)
            .AddEnvironmentVariables()
            .Build();

        RepositoryOptions = configuration.GetSection("RepositoryOptions").Get<TestRemoteRepositoryOptions>();

        var ariusConfig = new AriusConfiguration();
        configuration.Bind(ariusConfig);
        AriusConfiguration = Options.Create(ariusConfig);
    }

    public abstract void Dispose();
}

public record TestRemoteRepositoryOptions
{
    public string AccountName { get; init; }
    public string AccountKey { get; init; }
    public string ContainerName { get; set; }
    public string Passphrase { get; init; }
}