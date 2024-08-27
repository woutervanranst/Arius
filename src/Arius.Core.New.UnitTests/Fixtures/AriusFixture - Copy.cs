using System.IO.Abstractions;
using Arius.Core.Domain;
using Arius.Core.Domain.Repositories;
using Arius.Core.Domain.Services;
using Arius.Core.Domain.Storage;
using Arius.Core.Infrastructure.Repositories;
using Arius.Core.New.UnitTests.Fakes;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Arius.Core.New.UnitTests.Fixtures;

public class AriusFixture2
{
    public AriusFixture2()
    {
        //Setup the configuration
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddUserSecrets<AriusFixture2>(optional: true)
            .Build();

        //UnitTestRoot = new DirectoryInfo(Path.Combine(@"C:\AriusTest", $"UnitTests-{DateTime.Now:yyMMddHHmmss}-{Random.Shared.Next()}"));
        //UnitTestRoot.Create();

        // Setup the service collection
        var services = new ServiceCollection();

        // Add configuration to the service collection
        testRepositoryOptions = configuration.GetSection("RepositoryOptions").Get<TestRepositoryOptions>()!;

        // Register the actual services
        services.AddArius(c => c.LocalConfigRoot = FileSystem.DirectoryInfo.New("bla"));
        services.AddLogging();

        // Add additional services
        ConfigureServices(services, ServiceConfiguration.Mocked);

        // Build the service provider
        var serviceProvider = services.BuildServiceProvider();

        // Get the required services
        StorageAccountFactory = serviceProvider.GetRequiredService<IStorageAccountFactory>();
        Mediator              = serviceProvider.GetRequiredService<IMediator>();

        //StorageAccountFactory = Substitute.For<IStorageAccountFactory>();
        //FileSystem            = Substitute.For<IFileSystem>();
        //Config                = Substitute.For<AriusConfiguration>();

        //var options = Substitute.For<IOptions<AriusConfiguration>>();
        //options.Value.Returns(Config);

        Factory = new SqliteStateDbRepositoryFactory(StorageAccountFactory, options, FileSystem, NullLogger<SqliteStateDbRepositoryFactory>.Instance);

    }

    private void ConfigureServices(IServiceCollection services, ServiceConfiguration serviceConfiguration)
    {
        services.AddSingleton<IStorageAccountFactory>(Substitute.For<IStorageAccountFactory>());
        services.AddSingleton<ICryptoService, FakeCryptoService>();
    }

    public SqliteStateDbRepositoryFactory Factory               { get; }
    public IStorageAccountFactory         StorageAccountFactory { get; }
    public IFileSystem                    FileSystem            { get; }
    public IMediator                      Mediator              { get; }
    //public AriusConfiguration             Config                { get; }

    public IRepository           repository;
    public string                localPath;
    public TestRepositoryOptions testRepositoryOptions;
    public RepositoryOptions     repositoryOptions;
    public RepositoryVersion?    version;
    public IBlob                 blob;
}

public static class FixtureExtensions
{
    public static AriusFixture2 GivenRepositoryWithNoVersions(this AriusFixture2 fixture)
    {
        fixture.repository.GetRepositoryVersions().Returns(AsyncEnumerable.Empty<RepositoryVersion>());
        return fixture;
    }
}

public class SqliteStateDbRepositoryFactoryTests3
{
    private readonly AriusFixture2 fixture;

    public SqliteStateDbRepositoryFactoryTests3()
    {
        fixture = new AriusFixture2();
    }

    [Fact]
    public async Task CreateAsync_WhenNoVersionsAvailable_ShouldReturnNewFileName()
    {
        // Arrange 
        fixture.GivenRepositoryWithNoVersions();

        // Act
        var result = await fixture.Factory.CreateAsync(fixture.repositoryOptions);

    }
}



//public abstract class AriusFixture : IDisposable
//{
//    public Lazy<TestRepositoryOptions> mockedTestRepositoryOptions;
//    public Lazy<TestRepositoryOptions> realTestRepositoryOptions;

//    private Lazy<IMediator>              mockedMediator;
//    private Lazy<IStorageAccountFactory> mockedStorageAccountFactory;

//    private Lazy<IMediator>              realMediator;
//    private Lazy<IStorageAccountFactory> realStorageAccountFactory;

//    private Lazy<IServiceProvider> mockedServiceProvider;
//    private Lazy<IServiceProvider> realServiceProvider;

//    public DirectoryInfo UnitTestRoot { get; }


//    protected AriusFixture()
//    {
//        // Setup the configuration
//        var configuration = new ConfigurationBuilder()
//            .SetBasePath(Directory.GetCurrentDirectory())
//            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
//            .AddUserSecrets<AriusFixture>(optional: true)
//            .Build();

//        UnitTestRoot = new DirectoryInfo(Path.Combine(@"C:\AriusTest", $"UnitTests-{DateTime.Now:yyMMddHHmmss}-{Random.Shared.Next()}"));
//        UnitTestRoot.Create();

//        InitializeMockedServices();
//        InitializeRealServices();


//        void InitializeMockedServices()
//        {
//            // Setup the service collection
//            var services = new ServiceCollection();

//            // Add configuration to the service collection
//            mockedTestRepositoryOptions = new Lazy<TestRepositoryOptions>(configuration.GetSection("RepositoryOptions").Get<TestRepositoryOptions>()!);

//            // Register the actual services
//            services.AddArius(c => c.LocalConfigRoot = UnitTestRoot);
//            services.AddLogging();

//            // Add additional services
//            ConfigureServices(services, ServiceConfiguration.Mocked);

//            // Build the service provider
//            mockedServiceProvider = new Lazy<IServiceProvider>(services.BuildServiceProvider());

//            // Get the required services
//            mockedStorageAccountFactory = new Lazy<IStorageAccountFactory>(mockedServiceProvider.Value.GetRequiredService<IStorageAccountFactory>());
//            mockedMediator              = new Lazy<IMediator>(mockedServiceProvider.Value.GetRequiredService<IMediator>());
//        }

//        void InitializeRealServices()
//        {
//            // Setup the service collection
//            var services = new ServiceCollection();

//            // Add configuration to the service collection
//            realTestRepositoryOptions = new Lazy<TestRepositoryOptions>(configuration.GetSection("RepositoryOptions").Get<TestRepositoryOptions>()!);

//            // Register the actual services
//            services.AddArius(c => c.LocalConfigRoot = UnitTestRoot);
//            services.AddLogging();

//            // Add additional services
//            ConfigureServices(services, ServiceConfiguration.Real);

//            // Build the service provider
//            realServiceProvider = new Lazy<IServiceProvider>(services.BuildServiceProvider());

//            // Get the required services
//            realStorageAccountFactory = new Lazy<IStorageAccountFactory>(realServiceProvider.Value.GetRequiredService<IStorageAccountFactory>());
//            realMediator              = new Lazy<IMediator>(realServiceProvider.Value.GetRequiredService<IMediator>());
//        }
//    }

//    public IStorageAccountFactory GetStorageAccountFactory(ServiceConfiguration serviceConfiguration) =>
//        serviceConfiguration == ServiceConfiguration.Mocked ? 
//            mockedStorageAccountFactory.Value : 
//            realStorageAccountFactory.Value; 

//    public IMediator GetMediator(ServiceConfiguration serviceConfiguration) => 
//        serviceConfiguration == ServiceConfiguration.Mocked ? 
//            mockedMediator.Value : 
//            realMediator.Value;

//    public TestRepositoryOptions GetTestRepositoryOptions(ServiceConfiguration serviceConfiguration) => 
//        serviceConfiguration == ServiceConfiguration.Mocked ? 
//            mockedTestRepositoryOptions.Value : 
//            realTestRepositoryOptions.Value;

//    public StorageAccountOptions GetStorageAccountOptions(ServiceConfiguration serviceConfiguration) => 
//        new()
//        {
//            AccountName = GetTestRepositoryOptions(serviceConfiguration).AccountName,
//            AccountKey  = GetTestRepositoryOptions(serviceConfiguration).AccountKey
//        };

//    public RepositoryOptions GetRepositoryOptions(ServiceConfiguration serviceConfiguration) => 
//        new()
//        {
//            AccountName   = GetTestRepositoryOptions(serviceConfiguration).AccountName,
//            AccountKey    = GetTestRepositoryOptions(serviceConfiguration).AccountKey,
//            ContainerName = GetTestRepositoryOptions(serviceConfiguration).ContainerName,
//            Passphrase    = GetTestRepositoryOptions(serviceConfiguration).Passphrase
//        };

//    public IStateDbRepositoryFactory GetStateDbRepositoryFactory(ServiceConfiguration serviceConfiguration) =>
//        serviceConfiguration == ServiceConfiguration.Mocked ?
//            mockedServiceProvider.Value.GetRequiredService<IStateDbRepositoryFactory>() :
//            realServiceProvider.Value.GetRequiredService<IStateDbRepositoryFactory>();

//    public AriusConfiguration GetAriusConfiguration(ServiceConfiguration serviceConfiguration) =>
//        serviceConfiguration == ServiceConfiguration.Mocked ?
//            mockedServiceProvider.Value.GetRequiredService<IOptions<AriusConfiguration>>().Value :
//            realServiceProvider.Value.GetRequiredService<IOptions<AriusConfiguration>>().Value;

//    protected abstract void ConfigureServices(IServiceCollection services, ServiceConfiguration serviceConfiguration);

//    public void Dispose()
//    {
//        // Cleanup if necessary
//    }
//}

//public class RequestHandlerFixture : AriusFixture
//{
//    protected override void ConfigureServices(IServiceCollection services, ServiceConfiguration serviceConfiguration)
//    {
//        if (serviceConfiguration == ServiceConfiguration.Mocked)
//        {
//            // Substitute for the dependencies

//            services.AddSingleton<IStorageAccountFactory>(Substitute.For<IStorageAccountFactory>());
//            services.AddSingleton<ICryptoService, FakeCryptoService>();
//        }
//    }
//}

//public enum ServiceConfiguration
//{
//    Mocked,
//    Real
//}    