using Arius.Core.Features.Restore;
using Arius.Core.Tests.Helpers.Fixtures;

namespace Arius.Core.Tests.Helpers.Builders;

internal class RestoreCommandBuilder
{
    private string                     accountName;
    private string                     accountKey;
    private string                     containerName;
    private string                     passphrase;
    private string[]                   targets;
    private bool                       download;
    private bool                       includePointers;
    private DirectoryInfo              localRoot;
    private IProgress<ProgressUpdate>? progressReporter;

    public RestoreCommandBuilder()
    {
        SetDefaults(null);
    }

    public RestoreCommandBuilder(FixtureBase? fixture)
    {
        SetDefaults(fixture);
    }

    private void SetDefaults(FixtureBase? fixture)
    {
        if (fixture?.RepositoryOptions != null)
        {
            accountName   = fixture.RepositoryOptions.AccountName ?? "testaccount";
            accountKey    = fixture.RepositoryOptions.AccountKey ?? "testkey";
            containerName = $"{fixture.RepositoryOptions.ContainerName ?? "testcontainer"}-{DateTime.UtcNow.Ticks}-{Random.Shared.Next()}";
            passphrase    = fixture.RepositoryOptions.Passphrase;
            targets       = fixture is PhysicalFileSystemFixture physicalFixture ? [physicalFixture.TestRunSourceFolder.FullName] : ["/"];
        }
        else
        {
            accountName   = "testaccount";
            accountKey    = "testkey";
            containerName = $"testcontainer-{DateTime.UtcNow.Ticks}-{Random.Shared.Next()}";
            passphrase    = FixtureBase.PASSPHRASE;
            targets       = ["dummy"];
        }

        download         = false;
        includePointers  = false;
        localRoot        = new DirectoryInfo(Environment.CurrentDirectory);
        progressReporter = null;
    }

    public RestoreCommandBuilder WithAccountName(string accountName)
    {
        this.accountName = accountName;
        return this;
    }

    public RestoreCommandBuilder WithAccountKey(string accountKey)
    {
        this.accountKey = accountKey;
        return this;
    }

    public RestoreCommandBuilder WithContainerName(string containerName)
    {
        this.containerName = containerName;
        return this;
    }

    //public RestoreCommandBuilder WithPassphrase(string passphrase) // Passphrase is defaulted because of the FileSystemExtensions SHA shortcut
    //{
    //    this.passphrase = passphrase;
    //    return this;
    //}

    public RestoreCommandBuilder WithLocalRoot(DirectoryInfo localRoot)
    {
        this.localRoot = localRoot;
        return this;
    }

    public RestoreCommandBuilder WithTargets(params string[] targets)
    {
        this.targets = targets;
        return this;
    }

    public RestoreCommandBuilder WithDownload(bool download)
    {
        this.download = download;
        return this;
    }

    public RestoreCommandBuilder WithKeepPointers(bool keepPointers)
    {
        this.includePointers = keepPointers;
        return this;
    }

    public RestoreCommandBuilder WithProgressReporter(IProgress<ProgressUpdate>? progressReporter)
    {
        this.progressReporter = progressReporter;
        return this;
    }

    public RestoreCommand Build()
    {
        return new RestoreCommand
        {
            AccountName      = accountName,
            AccountKey       = accountKey,
            ContainerName    = containerName,
            Passphrase       = passphrase,
            Targets          = targets,
            Download         = download,
            IncludePointers  = includePointers,
            LocalRoot        = localRoot,
            ProgressReporter = progressReporter
        };
    }
}