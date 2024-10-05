using Arius.Core.Domain;
using Arius.Core.Domain.Repositories;
using Arius.Core.Domain.Services;
using Arius.Core.Domain.Storage;
using Arius.Core.Domain.Storage.FileSystem;
using Arius.Core.Infrastructure.Services;
using Arius.Core.New.UnitTests.Fakes;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Arius.Core.New.UnitTests.Fixtures;

public class FixtureBuilder
{
    private readonly IServiceCollection services;
    private readonly IConfigurationRoot configuration;
    private readonly DirectoryInfo      testRoot;
    private readonly DirectoryInfo      testRunRoot;

    private          TestRemoteRepositoryOptions testRemoteRepositoryOptions;
    private readonly IHashValueProvider          hashValueProvider;

    private readonly DirectoryInfo testRootSourceDirectory;

    public FixtureBuilder()
    {
        services = new ServiceCollection();
        configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddUserSecrets<FixtureBuilder>(optional: true)
            .Build();

        testRoot = new DirectoryInfo(@"C:\AriusTest");
        testRunRoot = testRoot.GetSubDirectory("UnitTestRuns").GetSubDirectory($"{DateTime.Now:yyMMddHHmmss}-{Random.Shared.Next()}").CreateIfNotExists();

        testRootSourceDirectory = testRoot.GetSubDirectory("Source").CreateIfNotExists();

        testRemoteRepositoryOptions = configuration.GetSection("RepositoryOptions").Get<TestRemoteRepositoryOptions>()!;

        hashValueProvider = new SHA256Hasher(testRemoteRepositoryOptions.Passphrase);

        services.AddArius(c => c.LocalConfigRoot = testRunRoot);
        services.AddLogging(configure =>
        {
            configure.AddConsole();
            configure.AddDebug();
        });

        services.AddSingleton<MediatorNotificationStore>();
    }


    // -- MEDIATR NOTIFICATIONS

    public FixtureBuilder WithMediatrNotificationStore<T>() where T : INotification
    {
        services.AddSingleton<GenericMediatrNotificationHandler<T>>();
        return this;
    }

    // -- STORAGE ACCOUNT

    public FixtureBuilder WithMockedStorageAccountFactory()
    {
        storageAccountFactoryIsConfigured = true;

        services.AddSingleton<IStorageAccountFactory>(Substitute.For<IStorageAccountFactory>());
        return this;
    }

    public FixtureBuilder WithRealStorageAccountFactory()
    {
        storageAccountFactoryIsConfigured = true;

        // Assuming the real implementation is registered by default in AddArius
        return this;
    }

    private bool storageAccountFactoryIsConfigured = false;


    // -- CONTAINER NAME

    public FixtureBuilder WithUniqueContainerName()
    {
        var containerName = $"test-{DateTime.UtcNow.Ticks}-{Random.Shared.Next()}";
        
        testRemoteRepositoryOptions = testRemoteRepositoryOptions with { ContainerName = containerName };
        return this;
    }


    // == CRYPTO SERVICE

    public FixtureBuilder WithFakeCryptoService()
    {
        services.AddSingleton<ICryptoService, FakeCryptoService>();
        return this;
    }

    public FixtureBuilder WithRealCryptoService()
    {
        // Assuming the real implementation is registered by default in AddArius
        return this;
    }

    
    // -- BUILD

    public AriusFixture Build()
    {
        if (!storageAccountFactoryIsConfigured)
            throw new InvalidOperationException($"StorageAccountFactory not set. Call {nameof(WithMockedStorageAccountFactory)} or {nameof(WithRealStorageAccountFactory)}.");

        var serviceProvider = services.BuildServiceProvider();

        return new AriusFixture(
            serviceProvider,
            hashValueProvider,
            testRemoteRepositoryOptions,
            testRootSourceDirectory,
            testRunRoot
        );
    }
}

public class AriusFixture : IDisposable
{
    public  IHashValueProvider          HashValueProvider           { get; }
    private TestRemoteRepositoryOptions TestRemoteRepositoryOptions { get; }
    public  DirectoryInfo               TestRootSourceFolder        { get; }
    public  DirectoryInfo               TestRunRootFolder           { get; }
    public  DirectoryInfo               TestRunSourceFolder         { get; }

    public  IStorageAccountFactory   StorageAccountFactory { get; }
    public  IRemoteRepository        RemoteRepository      { get; }
    public  IRemoteStateRepository   RemoteStateRepository { get; }
    public  IMediator                Mediator              { get; }
    public  AriusConfiguration       AriusConfiguration    { get; }
    public  IFileSystem              LocalFileSystem       { get; }
    
    private readonly MediatorNotificationStore mediatorNotifications;

    public AriusFixture(
        IServiceProvider serviceProvider,
        IHashValueProvider hashValueProvider,
        TestRemoteRepositoryOptions testRemoteRepositoryOptions,
        DirectoryInfo testRootSourceFolder,
        DirectoryInfo testRunRootFolder)
    {
        HashValueProvider           = hashValueProvider;
        TestRemoteRepositoryOptions = testRemoteRepositoryOptions;
        TestRootSourceFolder        = testRootSourceFolder;
        TestRunRootFolder           = testRunRootFolder;
        TestRunSourceFolder         = testRunRootFolder.GetSubDirectory("Source").CreateIfNotExists();

        StorageAccountFactory = serviceProvider.GetRequiredService<IStorageAccountFactory>();
        Mediator              = serviceProvider.GetRequiredService<IMediator>();
        AriusConfiguration    = serviceProvider.GetRequiredService<IOptions<AriusConfiguration>>().Value;
        LocalFileSystem       = serviceProvider.GetRequiredService<IFileSystem>();

        RemoteRepository      = StorageAccountFactory.GetRemoteRepository(RemoteRepositoryOptions);
        RemoteStateRepository = RemoteRepository.GetRemoteStateRepository();

        mediatorNotifications = serviceProvider.GetRequiredService<MediatorNotificationStore>();
    }
    
    public StorageAccountOptions StorageAccountOptions =>
        new()
        {
            AccountName = TestRemoteRepositoryOptions.AccountName,
            AccountKey  = TestRemoteRepositoryOptions.AccountKey
        };

    public RemoteRepositoryOptions RemoteRepositoryOptions =>
        new()
        {
            AccountName   = TestRemoteRepositoryOptions.AccountName,
            AccountKey    = TestRemoteRepositoryOptions.AccountKey,
            ContainerName = TestRemoteRepositoryOptions.ContainerName ?? throw new InvalidOperationException("ContainerName not set"),
            Passphrase    = TestRemoteRepositoryOptions.Passphrase
        };

    public IEnumerable<INotification> MediatorNotifications      => mediatorNotifications.Notifications;
    public void                       ClearMediatorNotifications() => mediatorNotifications.Clear();

    public void Dispose()
    {
        // Implement cleanup logic if necessary
    }
}