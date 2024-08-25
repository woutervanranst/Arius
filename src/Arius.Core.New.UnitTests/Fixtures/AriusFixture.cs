using Arius.Core.Domain.Storage;
using Arius.Core.New.Services;
using Arius.Core.New.UnitTests.Fakes;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Arius.Core.New.UnitTests.Fixtures;

public abstract class AriusFixture : IDisposable
{
    public Lazy<TestRepositoryOptions> mockedTestRepositoryOptions;
    public Lazy<TestRepositoryOptions> realTestRepositoryOptions;

    private Lazy<IMediator>              mockedMediator;
    private Lazy<IStorageAccountFactory> mockedStorageAccountFactory;

    private Lazy<IMediator>              realMediator;
    private Lazy<IStorageAccountFactory> realStorageAccountFactory;

    private Lazy<IServiceProvider> mockedServiceProvider;
    private Lazy<IServiceProvider> realServiceProvider;

    public DirectoryInfo UnitTestRoot { get; }


    protected AriusFixture()
    {
        // Setup the configuration
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddUserSecrets<AriusFixture>(optional: true)
            .Build();

        UnitTestRoot = new DirectoryInfo(Path.Combine(@"C:\AriusTest", $"UnitTests-{DateTime.Now:yyMMddHHmmss}-{Random.Shared.Next()}"));
        UnitTestRoot.Create();

        InitializeMockedServices();
        InitializeRealServices();


        void InitializeMockedServices()
        {
            // Setup the service collection
            var services = new ServiceCollection();

            // Add configuration to the service collection
            mockedTestRepositoryOptions = new Lazy<TestRepositoryOptions>(configuration.GetSection("RepositoryOptions").Get<TestRepositoryOptions>()!);

            // Register the actual services
            services.AddArius();
            services.AddLogging();

            // Add additional services
            ConfigureServices(services, ServiceConfiguration.Mocked);

            // Build the service provider
            mockedServiceProvider = new Lazy<IServiceProvider>(services.BuildServiceProvider());

            // Get the required services
            mockedStorageAccountFactory = new Lazy<IStorageAccountFactory>(mockedServiceProvider.Value.GetRequiredService<IStorageAccountFactory>());
            mockedMediator              = new Lazy<IMediator>(mockedServiceProvider.Value.GetRequiredService<IMediator>());
        }

        void InitializeRealServices()
        {
            // Setup the service collection
            var services = new ServiceCollection();

            // Add configuration to the service collection
            realTestRepositoryOptions = new Lazy<TestRepositoryOptions>(configuration.GetSection("RepositoryOptions").Get<TestRepositoryOptions>()!);

            // Register the actual services
            services.AddArius();
            services.AddLogging();

            // Add additional services
            ConfigureServices(services, ServiceConfiguration.Real);

            // Build the service provider
            realServiceProvider = new Lazy<IServiceProvider>(services.BuildServiceProvider());

            // Get the required services
            realStorageAccountFactory = new Lazy<IStorageAccountFactory>(realServiceProvider.Value.GetRequiredService<IStorageAccountFactory>());
            realMediator              = new Lazy<IMediator>(realServiceProvider.Value.GetRequiredService<IMediator>());
        }
    }

    public IStorageAccountFactory GetStorageAccountFactory(ServiceConfiguration serviceConfiguration) =>
        serviceConfiguration == ServiceConfiguration.Mocked ? 
            mockedStorageAccountFactory.Value : 
            realStorageAccountFactory.Value; 

    public IMediator GetMediator(ServiceConfiguration serviceConfiguration) => 
        serviceConfiguration == ServiceConfiguration.Mocked ? 
            mockedMediator.Value : 
            realMediator.Value;

    public TestRepositoryOptions GetTestRepositoryOptions(ServiceConfiguration serviceConfiguration) => 
        serviceConfiguration == ServiceConfiguration.Mocked ? 
            mockedTestRepositoryOptions.Value : 
            realTestRepositoryOptions.Value;

    public StorageAccountOptions GetStorageAccountOptions(ServiceConfiguration serviceConfiguration) => 
        new()
        {
            AccountName = GetTestRepositoryOptions(serviceConfiguration).AccountName,
            AccountKey  = GetTestRepositoryOptions(serviceConfiguration).AccountKey
        };

    public RepositoryOptions GetRepositoryOptions(ServiceConfiguration serviceConfiguration) => 
        new()
        {
            AccountName   = GetTestRepositoryOptions(serviceConfiguration).AccountName,
            AccountKey    = GetTestRepositoryOptions(serviceConfiguration).AccountKey,
            ContainerName = GetTestRepositoryOptions(serviceConfiguration).ContainerName,
            Passphrase    = GetTestRepositoryOptions(serviceConfiguration).Passphrase
        };

    protected abstract void ConfigureServices(IServiceCollection services, ServiceConfiguration serviceConfiguration);

    public void Dispose()
    {
        // Cleanup if necessary
    }
}

public class RequestHandlerFixture : AriusFixture
{
    protected override void ConfigureServices(IServiceCollection services, ServiceConfiguration serviceConfiguration)
    {
        if (serviceConfiguration == ServiceConfiguration.Mocked)
        {
            // Substitute for the dependencies

            services.AddSingleton<IStorageAccountFactory>(Substitute.For<IStorageAccountFactory>());
            services.AddSingleton<ICryptoService, FakeCryptoService>();
        }
    }
}

public enum ServiceConfiguration
{
    Mocked,
    Real
}    