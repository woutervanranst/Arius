using Arius.Core.Domain.Storage;
using Arius.Core.New.Services;
using Arius.Tests.Fixtures;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Arius.Core.New.UnitTests;

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


    protected AriusFixture()
    {
        // Setup the configuration
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddUserSecrets<AriusFixture>(optional: true)
            .Build();

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
            serviceConfiguration == ServiceConfiguration.Real ? 
                realStorageAccountFactory.Value : 
                throw new ArgumentOutOfRangeException(nameof(serviceConfiguration), serviceConfiguration, null);

    public IMediator GetMediator(ServiceConfiguration serviceConfiguration) => 
        serviceConfiguration == ServiceConfiguration.Mocked ? 
            mockedMediator.Value : serviceConfiguration == ServiceConfiguration.Real ? 
                realMediator.Value : 
                throw new ArgumentOutOfRangeException(nameof(serviceConfiguration), serviceConfiguration, null);

    public TestRepositoryOptions GetTestRepositoryOptions(ServiceConfiguration serviceConfiguration) => 
        serviceConfiguration == ServiceConfiguration.Mocked ? 
            mockedTestRepositoryOptions.Value : 
            serviceConfiguration == ServiceConfiguration.Real ? 
                realTestRepositoryOptions.Value : 
                throw new ArgumentOutOfRangeException(nameof(serviceConfiguration), serviceConfiguration, null);


    protected virtual void ConfigureServices(IServiceCollection services, ServiceConfiguration serviceConfiguration)
    {
        // Override this method to add additional services
    }

    public void Dispose()
    {
        // Cleanup if necessary
    }
}

public class CommandHandlerFixture : AriusFixture
{
    protected override void ConfigureServices(IServiceCollection services, ServiceConfiguration serviceConfiguration)
    {
        base.ConfigureServices(services, serviceConfiguration);

        if (serviceConfiguration == ServiceConfiguration.Mocked)
        {
            // Substitute for the dependencies

            services.AddSingleton<IStorageAccountFactory>(Substitute.For<IStorageAccountFactory>());
            services.AddSingleton<ICryptoService, MockCryptoService>();
        }
    }
}

public enum ServiceConfiguration
{
    Mocked,
    Real
}    