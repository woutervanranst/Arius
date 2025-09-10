using Arius.Core.Features.Archive;
using Arius.Core.Shared.Storage;
using Arius.Core.Tests.Helpers.Fixtures;

namespace Arius.Core.Tests.Helpers.Builders;

public class ArchiveCommandBuilder
{
    private string                     accountName;
    private string                     accountKey;
    private string                     containerName;
    private string                     passphrase;
    private bool                       removeLocal;
    private StorageTier                tier;
    private DirectoryInfo              localRoot;
    private int                        parallelism;
    private int                        smallFileBoundary;
    private IProgress<ProgressUpdate>? progressReporter;
    private bool                       useRetryPolicy;

    public ArchiveCommandBuilder()
    {
        SetDefaults(null);
    }

    public ArchiveCommandBuilder(FixtureWithFileSystem? fixture)
    {
        SetDefaults(fixture);
    }

    private void SetDefaults(FixtureWithFileSystem? fixture)
    {
        if (fixture?.RepositoryOptions != null)
        {
            accountName   = fixture.RepositoryOptions.AccountName ?? "testaccount";
            accountKey    = fixture.RepositoryOptions.AccountKey ?? "testkey";
            containerName = fixture.RepositoryOptions.ContainerName ?? "testcontainer";
            localRoot     = fixture.TestRunSourceFolder;
            passphrase    = fixture.RepositoryOptions.Passphrase ?? Fixture.PASSPHRASE;
        }
        else
        {
            accountName   = "testaccount";
            accountKey    = "testkey";
            containerName = $"testcontainer-{DateTime.UtcNow.Ticks}-{Random.Shared.Next()}";
            passphrase    = Fixture.PASSPHRASE;
            localRoot     = new DirectoryInfo(Path.GetTempPath());
        }

        removeLocal       = false;
        tier              = StorageTier.Cool;
        parallelism       = 1;
        smallFileBoundary = 2 * 1024 * 1024;
        progressReporter  = null;
        useRetryPolicy    = true;
    }

    public ArchiveCommandBuilder WithAccountName(string accountName)
    {
        this.accountName = accountName;
        return this;
    }

    public ArchiveCommandBuilder WithAccountKey(string accountKey)
    {
        this.accountKey = accountKey;
        return this;
    }

    public ArchiveCommandBuilder WithContainerName(string containerName)
    {
        this.containerName = containerName;
        return this;
    }

    //public RestoreCommandBuilder WithPassphrase(string passphrase) // Passphrase is defaulted because of the FileSystemExtensions SHA shortcut
    //{
    //    this.passphrase = passphrase;
    //    return this;
    //}

    public ArchiveCommandBuilder WithRemoveLocal(bool removeLocal)
    {
        this.removeLocal = removeLocal;
        return this;
    }

    public ArchiveCommandBuilder WithTier(StorageTier tier)
    {
        this.tier = tier;
        return this;
    }

    public ArchiveCommandBuilder WithLocalRoot(DirectoryInfo localRoot)
    {
        this.localRoot = localRoot;
        return this;
    }

    public ArchiveCommandBuilder WithParallelism(int parallelism)
    {
        this.parallelism = parallelism;
        return this;
    }

    public ArchiveCommandBuilder WithSmallFileBoundary(int smallFileBoundary)
    {
        this.smallFileBoundary = smallFileBoundary;
        return this;
    }

    public ArchiveCommandBuilder WithProgressReporter(IProgress<ProgressUpdate>? progressReporter)
    {
        this.progressReporter = progressReporter;
        return this;
    }

    public ArchiveCommandBuilder WithUseRetryPolicy(bool useRetryPolicy)
    {
        this.useRetryPolicy = useRetryPolicy;
        return this;
    }

    public ArchiveCommand Build()
    {
        return new ArchiveCommand
        {
            AccountName       = accountName,
            AccountKey        = accountKey,
            ContainerName     = containerName,
            Passphrase        = passphrase,
            RemoveLocal       = removeLocal,
            Tier              = tier,
            LocalRoot         = localRoot,
            Parallelism       = parallelism,
            SmallFileBoundary = smallFileBoundary,
            ProgressReporter  = progressReporter,
            UseRetryPolicy    = useRetryPolicy
        };
    }
}