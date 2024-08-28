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
using WouterVanRanst.Utils.Extensions;
using File = System.IO.File;

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
    private readonly IServiceCollection services;
    private readonly IConfigurationRoot configuration;
    private readonly DirectoryInfo      testRoot;
    private readonly DirectoryInfo      testRunRoot;
    
    private readonly DirectoryInfo         testRunSourceDirectory;
    private readonly TestRepositoryOptions testRepositoryOptions;
    private readonly IHashValueProvider    hashValueProvider;

    private DirectoryInfo         sourceDirectory;

    private FixtureBuilder()
    {
        services = new ServiceCollection();
        configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddUserSecrets<FixtureBuilder>(optional: true)
            .Build();

        testRoot = new DirectoryInfo(@"C:\AriusTest");
        testRunRoot = testRoot.GetSubDirectory("UnitTests").GetSubDirectory($"{DateTime.Now:yyMMddHHmmss}-{Random.Shared.Next()}").CreateIfNotExists();

        sourceDirectory = testRunRoot.GetSubDirectory("Source").CreateIfNotExists();

        testRunSourceDirectory = testRunRoot.GetSubDirectory("Source").CreateIfNotExists();

        testRepositoryOptions = configuration.GetSection("RepositoryOptions").Get<TestRepositoryOptions>()!;

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
        sourceDirectory.CopyTo(testRunSourceDirectory, recursive: true);

        return this;
    }

    public FixtureBuilder WithSourceFolderHavingRandomFile(string relativeName, long sizeInBytes, FileAttributes attributes = FileAttributes.Normal)
    {
        var filePath = Path.Combine(testRunSourceDirectory.FullName, relativeName);
        //var fileDirectory = Path.GetDirectoryName(filePath);
        //if (fileDirectory != null)
        //    Directory.CreateDirectory(fileDirectory);

        FileUtils.CreateRandomFile(filePath, sizeInBytes);
        SetAttributes(attributes, filePath);

        return this;
    }

    private static void SetAttributes(FileAttributes attributes, string filePath)
    {
        File.SetAttributes(filePath, attributes);

        var actualAtts = File.GetAttributes(filePath);
        if (actualAtts != attributes)
            throw new InvalidOperationException($"Could not set attributes for {filePath}");
    }

    public FixtureBuilder WithSourceFolderHavingRandomFileWithPointerFile(string relativeName, long sizeInBytes, FileAttributes attributes = FileAttributes.Normal)
    {
        WithSourceFolderHavingRandomFile(relativeName, sizeInBytes, attributes);

        var bf   = BinaryFile.FromRelativeName(testRunSourceDirectory, relativeName);
        var h    = hashValueProvider.GetHashAsync(bf).Result;
        var bfwh = bf.GetBinaryFileWithHash(h);
        var pfwh = bfwh.GetPointerFileWithHash();
        pfwh.Save();

        return this;
    }

    public AriusFixture Build()
    {
        var serviceProvider = services.BuildServiceProvider();

        return new AriusFixture(
            serviceProvider,
            testRepositoryOptions,
            sourceDirectory,
            testRunRoot
        );
    }
}

public class AriusFixture : IDisposable
{
    private TestRepositoryOptions TestRepositoryOptions { get; }
    public  DirectoryInfo         SourceFolder          { get; }
    public  DirectoryInfo         TestRunRootFolder     { get; }

    public  IStorageAccountFactory    StorageAccountFactory    { get; }
    public  IStateDbRepositoryFactory StateDbRepositoryFactory { get; }
    public  IMediator                 Mediator                 { get; }
    public  AriusConfiguration        AriusConfiguration       { get; }

    public AriusFixture(
        IServiceProvider serviceProvider,
        TestRepositoryOptions testRepositoryOptions,
        DirectoryInfo sourceFolder,
        DirectoryInfo testRunRootFolder)
    {
        TestRepositoryOptions = testRepositoryOptions;
        SourceFolder          = sourceFolder;
        TestRunRootFolder     = testRunRootFolder;

        StorageAccountFactory    = serviceProvider.GetRequiredService<IStorageAccountFactory>();
        StateDbRepositoryFactory = serviceProvider.GetRequiredService<IStateDbRepositoryFactory>();
        Mediator                 = serviceProvider.GetRequiredService<IMediator>();
        AriusConfiguration       = serviceProvider.GetRequiredService<IOptions<AriusConfiguration>>().Value;
    }



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



    public void Dispose()
    {
        // Implement cleanup logic if necessary
    }
}