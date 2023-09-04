using System.IO;
using Arius.UI.Extensions;
using Arius.UI.Utils;

namespace Arius.UI.ViewModels;

internal interface IRepositoryOptions
{
    DirectoryInfo LocalDirectory { get; }

    string AccountName   { get; }
    string AccountKey    { get; }
    string ContainerName { get; }
    string Passphrase    { get; }
}

internal record RepositoryChosenMessage : IRepositoryOptions
{
    public RepositoryChosenMessage(object sender, RepositoryOptionsDto repo)
    {
        // The Repository is chosen from the database / recent repositories
        this.Sender = sender;

        LocalDirectory = new DirectoryInfo(repo.LocalDirectory);
        AccountName    = repo.AccountName;
        AccountKey     = repo.AccountKeyProtected.Unprotect();
        ContainerName  = repo.ContainerName;
        Passphrase     = repo.PassphraseProtected.Unprotect();
    }
    public RepositoryChosenMessage(ChooseRepositoryViewModel viewModel)
    {
        // The Repository is chosen from the RepositoryChooserWindow
        this.Sender = viewModel;

        LocalDirectory = new DirectoryInfo(viewModel.LocalDirectory);
        AccountName    = viewModel.AccountName;
        AccountKey     = viewModel.AccountKey;
        ContainerName  = viewModel.SelectedContainerName;
        Passphrase     = viewModel.Passphrase;
    }

    public object Sender { get; }

    public DirectoryInfo LocalDirectory { get; }

    public string AccountName   { get; }
    public string AccountKey    { get; }
    public string ContainerName { get; }
    public string Passphrase    { get; }
}

internal record ChooseRepositoryMessage : IRepositoryOptions
{
    public ChooseRepositoryMessage()
    {
        // Show the ChooseRepositoryWindow without any prefilled text
    }

    public ChooseRepositoryMessage(IRepositoryOptions options)
    {
        LocalDirectory = options.LocalDirectory;
        AccountName    = options.AccountName;
        AccountKey     = options.AccountKey;
        ContainerName  = options.ContainerName;
        Passphrase     = options.Passphrase;
    }

    //public required object Sender { get; init; }

    public DirectoryInfo? LocalDirectory { get; }

    public string? AccountName   { get; }
    public string? AccountKey    { get; }
    public string? ContainerName { get; }
    public string? Passphrase    { get;  }
}