using Arius.Core.Domain;
using Arius.Core.Domain.Repositories;
using Arius.Core.Domain.Services;
using Arius.Core.Domain.Storage;
using Arius.Core.New.UnitTests.Fakes;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Arius.Core.New.UnitTests.Fixtures;

using System;

public interface IAriusFixture : IDisposable
{
    DirectoryInfo            UnitTestRoot             { get; }
    IStorageAccountFactory    StorageAccountFactory    { get; }
    IMediator                 Mediator                 { get; }
    TestRepositoryOptions     TestRepositoryOptions    { get; }
    StorageAccountOptions     StorageAccountOptions    { get; }
    RepositoryOptions         RepositoryOptions        { get; }
    IStateDbRepositoryFactory StateDbRepositoryFactory { get; }
    AriusConfiguration        AriusConfiguration       { get; }
}

public class MockAriusFixture : IAriusFixture
{
    public DirectoryInfo         UnitTestRoot          { get; }
    public IStorageAccountFactory StorageAccountFactory { get; }
    public IMediator              Mediator              { get; }
    public TestRepositoryOptions  TestRepositoryOptions { get; }

    public StorageAccountOptions StorageAccountOptions =>
        new()
        {
            AccountName = TestRepositoryOptions.AccountName,
            AccountKey  = TestRepositoryOptions.AccountKey
        };

    public RepositoryOptions RepositoryOptions =>
        new()
        {
            AccountName   = TestRepositoryOptions.AccountName,
            AccountKey    = TestRepositoryOptions.AccountKey,
            ContainerName = TestRepositoryOptions.ContainerName,
            Passphrase    = TestRepositoryOptions.Passphrase
        };

    public IStateDbRepositoryFactory StateDbRepositoryFactory { get; }
    public AriusConfiguration        AriusConfiguration       { get; }

    public MockAriusFixture()
    {
        // Setup the configuration
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddUserSecrets<MockAriusFixture>(optional: true)
            .Build();

        UnitTestRoot = new DirectoryInfo(Path.Combine(@"C:\AriusTest", $"UnitTests-{DateTime.Now:yyMMddHHmmss}-{Random.Shared.Next()}"));
        UnitTestRoot.Create();

        // Setup the service collection
        var services = new ServiceCollection();

        // Add configuration to the service collection
        TestRepositoryOptions = configuration.GetSection("RepositoryOptions").Get<TestRepositoryOptions>()!;

        // Register the mocked services
        services.AddArius(c => c.LocalConfigRoot = UnitTestRoot);
        services.AddLogging();

        // Add additional services
        ConfigureServices(services);

        // Build the service provider
        var serviceProvider = services.BuildServiceProvider();

        // Get the required services
        StorageAccountFactory    = serviceProvider.GetRequiredService<IStorageAccountFactory>();
        Mediator                 = serviceProvider.GetRequiredService<IMediator>();
        StateDbRepositoryFactory = serviceProvider.GetRequiredService<IStateDbRepositoryFactory>();
        AriusConfiguration       = serviceProvider.GetRequiredService<IOptions<AriusConfiguration>>().Value;
    }

    protected virtual void ConfigureServices(IServiceCollection services)
    {
        // Add mocked services here
        services.AddSingleton<IStorageAccountFactory>(Substitute.For<IStorageAccountFactory>());
        services.AddSingleton<ICryptoService, FakeCryptoService>();
    }

    public void Dispose()
    {
        // Cleanup if necessary
    }
}

public class RealAriusFixture : IAriusFixture
{
    private readonly IServiceProvider serviceProvider;

    public DirectoryInfo         UnitTestRoot          { get; }
    public IStorageAccountFactory StorageAccountFactory { get; }
    public IMediator              Mediator              { get; }
    public TestRepositoryOptions  TestRepositoryOptions { get; }
    public StorageAccountOptions StorageAccountOptions =>
        new()
        {
            AccountName = TestRepositoryOptions.AccountName,
            AccountKey = TestRepositoryOptions.AccountKey
        };
    public RepositoryOptions RepositoryOptions =>
        new()
        {
            AccountName = TestRepositoryOptions.AccountName,
            AccountKey = TestRepositoryOptions.AccountKey,
            ContainerName = TestRepositoryOptions.ContainerName,
            Passphrase = TestRepositoryOptions.Passphrase
        };
    public IStateDbRepositoryFactory StateDbRepositoryFactory { get; }
    public AriusConfiguration AriusConfiguration { get; }

    public RealAriusFixture()
    {
        // Setup the configuration
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddUserSecrets<RealAriusFixture>(optional: true)
            .Build();

        UnitTestRoot = new DirectoryInfo(Path.Combine(@"C:\AriusTest", $"UnitTests-{DateTime.Now:yyMMddHHmmss}-{Random.Shared.Next()}"));
        UnitTestRoot.Create();

        // Setup the service collection
        var services = new ServiceCollection();

        // Add configuration to the service collection
        TestRepositoryOptions = configuration.GetSection("RepositoryOptions").Get<TestRepositoryOptions>()!;

        // Register the real services
        services.AddArius(c => c.LocalConfigRoot = UnitTestRoot);
        services.AddLogging();

        // Add additional services
        ConfigureServices(services);

        // Build the service provider
        serviceProvider = services.BuildServiceProvider();

        // Get the required services
        StorageAccountFactory = serviceProvider.GetRequiredService<IStorageAccountFactory>();
        Mediator = serviceProvider.GetRequiredService<IMediator>();
        StateDbRepositoryFactory = serviceProvider.GetRequiredService<IStateDbRepositoryFactory>();
        AriusConfiguration = serviceProvider.GetRequiredService<IOptions<AriusConfiguration>>().Value;
    }

    protected virtual void ConfigureServices(IServiceCollection services)
    {
        // Add real services here
    }

    public void Dispose()
    {
        // Cleanup if necessary
    }
}