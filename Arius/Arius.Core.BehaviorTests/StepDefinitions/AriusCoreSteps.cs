using Arius.Core.Commands;
using BoDi;
using Microsoft.Extensions.Logging;
using System;
using TechTalk.SpecFlow;
using Microsoft.Extensions.Options;
using Arius.Core.Configuration;
using Arius.Core.Commands.Archive;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.DependencyInjection;
using Azure.Storage.Blobs;
using Arius.Core.Repositories;

namespace Arius.Core.BehaviorTests.StepDefinitions;

[Binding]
class AriusCoreSteps
{
    [BeforeTestRun]
    public static void InitializeFacade(IObjectContainer oc)
    {
        oc.RegisterFactoryAs<Facade>((_) =>
        {
            var loggerFactory = LoggerFactory.Create(builder => builder.AddDebug());

            var tempDirectoryAppSettings = Options.Create(new TempDirectoryAppSettings()
            {
                TempDirectoryName = ".ariustemp",
                RestoreTempDirectoryName = ".ariusrestore"
            });

            return new Facade(loggerFactory, tempDirectoryAppSettings);
        });
    }

    public AriusCoreSteps(ScenarioContext sc, RepositoryOptions ro, BlobContainerClient bcc, Directories directories)
    {
        this.scenarioContext = sc;
        repositoryOptions = ro;
        this.container = bcc;
        this.directories = directories;
    }

    private readonly ScenarioContext scenarioContext;
    private readonly RepositoryOptions repositoryOptions;
    private readonly BlobContainerClient container;
    private readonly Directories directories;

    [BeforeScenario]
    public void ClearDirectories()
    {
        BlockBase.Reset();
    }


    private record ArchiveCommandOptions : IArchiveCommandOptions
    {
        public string AccountName { get; init; }
        public string AccountKey { get; init; }
        public string Container { get; init; }
        public string Passphrase { get; init; }
        public bool FastHash { get; init; }
        public bool RemoveLocal { get; init; }
        public AccessTier Tier { get; init; }
        public bool Dedup { get; init; }
        public DirectoryInfo Path { get; init; }
        public DateTime VersionUtc { get; init; }
    }

    [When("archived to the {word} tier")]
    public async Task WhenArchived(string tier) => await ArchiveCommandAsync((AccessTier)tier);
    [When("archived to the {word} tier with option RemoveLocal")]
    public async Task WhenArchivedRemoveLocal(string tier) => await ArchiveCommandAsync((AccessTier)tier, removeLocal: true);
    private async Task ArchiveCommandAsync(AccessTier tier, bool purgeRemote = false, bool removeLocal = false, bool fastHash = false, bool dedup = false)
    {
        if (purgeRemote)
            await RemoteRepositorySteps.PurgeRemote(container);

        var sp = new ServiceCollection()
            .AddAriusCoreCommands()
            .AddLogging()
            .BuildServiceProvider();
        var archiveCommand = sp.GetRequiredService<ICommand<IArchiveCommandOptions>>();


        var options = new ArchiveCommandOptions
        {
            AccountName = repositoryOptions.AccountName,
            AccountKey = repositoryOptions.AccountKey,
            Container = repositoryOptions.Container,
            Dedup = dedup,
            FastHash = fastHash,
            Passphrase = repositoryOptions.Passphrase,
            Path = directories.ArchiveTestDirectory,
            RemoveLocal = removeLocal,
            Tier = tier,
            VersionUtc = DateTime.UtcNow
        };

        await archiveCommand.ExecuteAsync(options);

        await scenarioContext.AddRemoteRepoStatsAsync();
        scenarioContext.AddLocalRepoStats();
    }
}
