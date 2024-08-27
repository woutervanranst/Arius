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
    //DirectoryInfo             UnitTestRoot             { get; }
    IStorageAccountFactory StorageAccountFactory { get; }

    IMediator Mediator { get; }

    //TestRepositoryOptions     TestRepositoryOptions    { get; }
    StorageAccountOptions     StorageAccountOptions    { get; }
    RepositoryOptions         RepositoryOptions        { get; }
    IStateDbRepositoryFactory StateDbRepositoryFactory { get; }
    AriusConfiguration        AriusConfiguration       { get; }
}

public class FixtureBuilder
{
    private readonly IServiceCollection    services;
    private readonly IConfigurationRoot    configuration;
    private readonly DirectoryInfo         unitTestRoot;
    private          TestRepositoryOptions testRepositoryOptions;

    private FixtureBuilder()
    {
        services = new ServiceCollection();
        configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddUserSecrets<FixtureBuilder>(optional: true)
            .Build();

        unitTestRoot = new DirectoryInfo(Path.Combine(@"C:\AriusTest", $"UnitTests-{DateTime.Now:yyMMddHHmmss}-{Random.Shared.Next()}"));
        unitTestRoot.Create();

        testRepositoryOptions = configuration.GetSection("RepositoryOptions").Get<TestRepositoryOptions>()!;

        services.AddArius(c => c.LocalConfigRoot = unitTestRoot);
        services.AddLogging();
    }

    public static FixtureBuilder Create() => new FixtureBuilder();

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

    public IAriusFixture Build()
    {
        var serviceProvider = services.BuildServiceProvider();

        return new AriusFixture(
            unitTestRoot,
            serviceProvider.GetRequiredService<IStorageAccountFactory>(),
            serviceProvider.GetRequiredService<IMediator>(),
            testRepositoryOptions,
            serviceProvider.GetRequiredService<IStateDbRepositoryFactory>(),
            serviceProvider.GetRequiredService<IOptions<AriusConfiguration>>().Value
        );
    }
}

public class AriusFixture : IAriusFixture
{
    public DirectoryInfo             UnitTestRoot             { get; }
    public IStorageAccountFactory    StorageAccountFactory    { get; }
    public IMediator                 Mediator                 { get; }
    public TestRepositoryOptions     TestRepositoryOptions    { get; }
    public IStateDbRepositoryFactory StateDbRepositoryFactory { get; }
    public AriusConfiguration        AriusConfiguration       { get; }

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

    public AriusFixture(
        DirectoryInfo unitTestRoot,
        IStorageAccountFactory storageAccountFactory,
        IMediator mediator,
        TestRepositoryOptions testRepositoryOptions,
        IStateDbRepositoryFactory stateDbRepositoryFactory,
        AriusConfiguration ariusConfiguration)
    {
        UnitTestRoot             = unitTestRoot;
        StorageAccountFactory    = storageAccountFactory;
        Mediator                 = mediator;
        TestRepositoryOptions    = testRepositoryOptions;
        StateDbRepositoryFactory = stateDbRepositoryFactory;
        AriusConfiguration       = ariusConfiguration;
    }

    public void Dispose()
    {
        // Implement cleanup logic if necessary
    }
}