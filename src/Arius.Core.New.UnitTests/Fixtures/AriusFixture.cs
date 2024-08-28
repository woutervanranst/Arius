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
using WouterVanRanst.Utils.Extensions;

namespace Arius.Core.New.UnitTests.Fixtures;

using System;

//public interface IAriusFixture : IDisposable
//{
//    IStorageAccountFactory    StorageAccountFactory    { get; }
//    StorageAccountOptions     StorageAccountOptions    { get; }
//    IMediator                 Mediator                 { get; }
//    RepositoryOptions         RepositoryOptions        { get; }
//    IStateDbRepositoryFactory StateDbRepositoryFactory { get; }
//    AriusConfiguration        AriusConfiguration       { get; }
//    DirectoryInfo             SourceFolder             { get; }
//}

public class FixtureBuilder
{
    private readonly IServiceCollection    services;
    private readonly IConfigurationRoot    configuration;
    private readonly DirectoryInfo         testRoot;
    private readonly DirectoryInfo         testRunRoot;
    private          DirectoryInfo         sourceDirectory;
    private          TestRepositoryOptions testRepositoryOptions;

    private FixtureBuilder()
    {
        services = new ServiceCollection();
        configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddUserSecrets<FixtureBuilder>(optional: true)
            .Build();

        testRoot = new DirectoryInfo(@"C:\AriusTest");
        testRunRoot = testRoot.GetSubDirectory("UnitTests").GetSubDirectory($"{DateTime.Now:yyMMddHHmmss}-{Random.Shared.Next()}");
        testRunRoot.Create();

        testRepositoryOptions = configuration.GetSection("RepositoryOptions").Get<TestRepositoryOptions>()!;

        services.AddArius(c => c.LocalConfigRoot = testRunRoot);
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

    public FixtureBuilder WithPopulatedSourceFolder()
    {
        if (testRoot.GetSubDirectory("Source") is { Exists: true } testRootSourceDirectory)
        {
            sourceDirectory = testRunRoot.GetSubDirectory("Source");
            testRootSourceDirectory.CopyTo(sourceDirectory, recursive: true);
        }
        else
            throw new InvalidOperationException("Source folder empty");

        return this;
    }

    public record FileDescription(string RelativePath, long SizeInBytes, FileAttributes Attributes);

    public FixtureBuilder WithSourceFolderHaving(params FileDescription[] files)
    {
        sourceDirectory = testRunRoot.GetSubDirectory("Source");
        sourceDirectory.Create();

        foreach (var (relativePath, sizeInBytes, attributes) in files)
        {
            var filePath      = Path.Combine(sourceDirectory.FullName, relativePath);
            var fileDirectory = Path.GetDirectoryName(filePath);

            if (fileDirectory != null)
            {
                Directory.CreateDirectory(fileDirectory);
            }

            FileUtils.CreateRandomFile(filePath, sizeInBytes);
            File.SetAttributes(filePath, attributes);

            var actualAtts = File.GetAttributes(filePath);
            if (actualAtts != attributes)
                throw new InvalidOperationException($"Could not set attributes for {filePath}");
        }

        return this;
    }

    public AriusFixture Build()
    {
        var serviceProvider = services.BuildServiceProvider();

        return new AriusFixture(
            serviceProvider.GetRequiredService<IStorageAccountFactory>(),
            serviceProvider.GetRequiredService<IStateDbRepositoryFactory>(),
            serviceProvider.GetRequiredService<IMediator>(),
            serviceProvider.GetRequiredService<IOptions<AriusConfiguration>>().Value,
            testRepositoryOptions,
            sourceDirectory
        );
    }
}

public class AriusFixture : IDisposable
{
    public IStorageAccountFactory    StorageAccountFactory    { get; }
    public IStateDbRepositoryFactory StateDbRepositoryFactory { get; }
    public IMediator                 Mediator                 { get; }
    public AriusConfiguration        AriusConfiguration       { get; }
    public DirectoryInfo             SourceFolder             { get; }

    private readonly TestRepositoryOptions testRepositoryOptions;


    public AriusFixture(
        IStorageAccountFactory storageAccountFactory,
        IStateDbRepositoryFactory stateDbRepositoryFactory,
        IMediator mediator,
        AriusConfiguration ariusConfiguration,
        TestRepositoryOptions testRepositoryOptions,
        DirectoryInfo sourceFolder)
    {
        StorageAccountFactory      = storageAccountFactory;
        StateDbRepositoryFactory   = stateDbRepositoryFactory;
        Mediator                   = mediator;
        AriusConfiguration         = ariusConfiguration;
        this.testRepositoryOptions = testRepositoryOptions;
        SourceFolder               = sourceFolder;
    }


    public StorageAccountOptions StorageAccountOptions =>
        new()
        {
            AccountName = testRepositoryOptions.AccountName,
            AccountKey  = testRepositoryOptions.AccountKey
        };

    public RepositoryOptions RepositoryOptions =>
        new()
        {
            AccountName   = testRepositoryOptions.AccountName,
            AccountKey    = testRepositoryOptions.AccountKey,
            ContainerName = testRepositoryOptions.ContainerName,
            Passphrase    = testRepositoryOptions.Passphrase
        };



    public void Dispose()
    {
        // Implement cleanup logic if necessary
    }
}