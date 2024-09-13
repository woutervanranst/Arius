using Arius.Core.Domain;
using Arius.Core.Domain.Repositories;
using Arius.Core.Domain.Services;
using Arius.Core.Domain.Storage;
using Arius.Core.Infrastructure.Services;
using Arius.Core.New.UnitTests.Fakes;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Arius.Core.New.UnitTests.Fixtures;

using System;

public class FixtureBuilder
{
    private readonly IServiceCollection services;
    private readonly IConfigurationRoot configuration;
    private readonly DirectoryInfo      testRoot;
    private readonly DirectoryInfo      testRunRoot;

    private          TestRemoteRepositoryOptions testRemoteRepositoryOptions;
    private readonly IHashValueProvider    hashValueProvider;

    private DirectoryInfo         testRootSourceDirectory;

    private FixtureBuilder()
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

        hashValueProvider = new SHA256Hasher("unittest");

        services.AddArius(c => c.LocalConfigRoot = testRunRoot);
        services.AddLogging();
    }

    public static FixtureBuilder Create() => new();

    public FixtureBuilder WithMockedStorageAccountFactory()
    {
        services.AddSingleton<IStorageAccountFactory>(Substitute.For<IStorageAccountFactory>());
        return this;
    }

    public FixtureBuilder WithRealStorageAccountFactory()
    {
        // Assuming the real implementation is registered by default in AddArius
        return this;
    }

    public FixtureBuilder WithUniqueContainerName()
    {
        var containerName = $"test-{DateTime.UtcNow.Ticks}-{Random.Shared.Next()}";
        
        testRemoteRepositoryOptions = testRemoteRepositoryOptions with { ContainerName = containerName };
        return this;
    }

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

    public AriusFixture Build()
    {
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
    public  IHashValueProvider    HashValueProvider     { get; }
    private TestRemoteRepositoryOptions TestRemoteRepositoryOptions { get; }
    public  DirectoryInfo         TestRootSourceFolder          { get; }
    public  DirectoryInfo         TestRunRootFolder     { get; }
    public  DirectoryInfo         TestRunSourceFolder   { get; }

    public IStorageAccountFactory    StorageAccountFactory    { get; }
    public IRemoteRepository               RemoteRepository               { get; }
    public IRemoteStateRepository RemoteStateRepository { get; }
    public IMediator                 Mediator                 { get; }
    public AriusConfiguration        AriusConfiguration       { get; }

    public AriusFixture(
        IServiceProvider serviceProvider,
        IHashValueProvider hashValueProvider,
        TestRemoteRepositoryOptions testRemoteRepositoryOptions,
        DirectoryInfo testRootSourceFolder,
        DirectoryInfo testRunRootFolder)
    {
        HashValueProvider     = hashValueProvider;
        TestRemoteRepositoryOptions = testRemoteRepositoryOptions;
        TestRootSourceFolder  = testRootSourceFolder;
        TestRunRootFolder     = testRunRootFolder;
        TestRunSourceFolder   = testRunRootFolder.GetSubDirectory("Source").CreateIfNotExists();

        StorageAccountFactory    = serviceProvider.GetRequiredService<IStorageAccountFactory>();
        RemoteStateRepository = serviceProvider.GetRequiredService<IRemoteStateRepository>();
        Mediator                 = serviceProvider.GetRequiredService<IMediator>();
        AriusConfiguration       = serviceProvider.GetRequiredService<IOptions<AriusConfiguration>>().Value;

        RemoteRepository = StorageAccountFactory.GetRemoteRepository(RemoteRepositoryOptions);
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

    public void Dispose()
    {
        // Implement cleanup logic if necessary
    }
}