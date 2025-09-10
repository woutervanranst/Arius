using Arius.Core.Features.Restore;
using Arius.Core.Tests.Helpers.Fixtures;

namespace Arius.Core.Tests.Helpers.Builders;

internal class RestoreCommandBuilder
{
    private string                                                      accountName;
    private string                                                      accountKey;
    private string                                                      containerName;
    private string                                                      passphrase;
    private string[]                                                    targets;
    private bool                                                        download;
    private bool                                                        includePointers;
    private DirectoryInfo                                               localRoot;
    private IProgress<ProgressUpdate>?                                  progressReporter;
    private Func<IReadOnlyList<RehydrationDetail>, RehydrationDecision> rehydrationQuestionHandler;

    public RestoreCommandBuilder()
    {
        SetDefaults(null);
    }

    public RestoreCommandBuilder(FixtureWithFileSystem? fixture)
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
            passphrase    = fixture.RepositoryOptions.Passphrase;
            targets       = [fixture.TestRunSourceFolder.FullName];
        }
        else
        {
            accountName   = "testaccount";
            accountKey    = "testkey";
            containerName = $"testcontainer-{DateTime.UtcNow.Ticks}-{Random.Shared.Next()}";
            passphrase    = Fixture.PASSPHRASE;
            targets       = ["dummy"];
        }

        download                   = false;
        includePointers            = false;
        localRoot                  = new DirectoryInfo(Environment.CurrentDirectory);
        progressReporter           = null;
        rehydrationQuestionHandler = _ => RehydrationDecision.StandardPriority;
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

    public RestoreCommandBuilder WithIncludePointers(bool includePointers)
    {
        this.includePointers = includePointers;
        return this;
    }

    public RestoreCommandBuilder WithProgressReporter(IProgress<ProgressUpdate>? progressReporter)
    {
        this.progressReporter = progressReporter;
        return this;
    }

    public RestoreCommandBuilder WithRehydrationQuestionHandler(Func<IReadOnlyList<RehydrationDetail>, RehydrationDecision> rehydrationQuestionHandler)
    {
        this.rehydrationQuestionHandler = rehydrationQuestionHandler;
        return this;
    }

    public RestoreCommand Build()
    {
        return new RestoreCommand
        {
            AccountName                = accountName,
            AccountKey                 = accountKey,
            ContainerName              = containerName,
            Passphrase                 = passphrase,
            Targets                    = targets,
            Download                   = download,
            IncludePointers            = includePointers,
            LocalRoot                  = localRoot,
            ProgressReporter           = progressReporter,
            RehydrationQuestionHandler = rehydrationQuestionHandler
        };
    }
}