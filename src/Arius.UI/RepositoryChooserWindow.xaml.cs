using Arius.Core.Facade;
using Arius.UI.Properties;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Arius.UI;

/// <summary>
/// Interaction logic for RepositoryChooserWindow.xaml
/// </summary>
public partial class RepositoryChooserWindow : Window
{
    public RepositoryChooserWindow()
    {
        InitializeComponent();
    }

    private void RepositoryChooserWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is ChooseRepositoryViewModel viewModel)
        {
            AccountKeyPasswordBox.Password = viewModel.AccountKey;
        }
    }

    private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is ChooseRepositoryViewModel viewModel)
        {
            viewModel.AccountKey = ((PasswordBox)sender).Password;
        }
    }
}

public class ChooseRepositoryViewModel : ObservableObject
{
    private readonly Facade    facade;
    private readonly Debouncer debouncer = new();


    public ChooseRepositoryViewModel(Facade facade)
    {
        this.facade = facade;

        LoadState();

        //LoadContainersCommand       = new RelayCommand(async () => await LoadContainersAsync(), CanLoadContainers);
        SelectLocalDirectoryCommand = new RelayCommand(SelectLocalDirectory);
        OpenRepositoryCommand       = new RelayCommand(OpenRepository, CanOpenRepository);

        //=> new RelayCommand(OpenRepository, CanOpenRepository);
    }
    

    public string LocalDirectory
    {
        get => localDirectory;
        set => SetProperty(ref localDirectory, value);
    }
    private string localDirectory;


    public ICommand SelectLocalDirectoryCommand { get; }
    private void SelectLocalDirectory()
    {
        using (var dialog = new FolderBrowserDialog())
        {
            dialog.SelectedPath = LocalDirectory;

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                LocalDirectory = dialog.SelectedPath;
            }
        }
    }


    public string AccountName
    {
        get => accountName;
        set
        {
            if (SetProperty(ref accountName, value))
            {
                debouncer.Debounce(async () =>
                {
                    if (CanLoadContainers()) await LoadContainersAsync();
                });
            }
        }
    }
    private string accountName;


    public string AccountKey
    {
        get => accountKey;
        set
        {
            if (SetProperty(ref accountKey, value))
            {
                debouncer.Debounce(async () =>
                {
                    if (CanLoadContainers()) await LoadContainersAsync();
                });
            }
        }
    }
    private string accountKey;

    
    public bool AccountError
    {
        get => accountError;
        set => SetProperty(ref accountError, value);
    }
    private bool accountError;


    private bool CanLoadContainers()
    {
        return !string.IsNullOrWhiteSpace(AccountName) && !string.IsNullOrWhiteSpace(AccountKey);
    }


    private async Task LoadContainersAsync()
    {
        try
        {
            var storageFacade = facade.ForStorageAccount(AccountName, AccountKey);
            ContainerNames = new ObservableCollection<string>(await storageFacade.GetContainerNamesAsync(0).ToListAsync());

            if (ContainerNames.Count > 0)
            {
                SelectedContainerName = ContainerNames[0];
            }

            AccountError = false;
        }
        catch (Exception e)
        {
            AccountError = true;
        }
    }


    public ObservableCollection<string> ContainerNames
    {
        get => containerNames;
        set => SetProperty(ref containerNames, value);
    }
    private ObservableCollection<string> containerNames;


    public string SelectedContainerName
    {
        get => selectedContainerName;
        set => SetProperty(ref selectedContainerName, value);
    }
    private string selectedContainerName;


    public ICommand OpenRepositoryCommand { get; }
    private bool CanOpenRepository()
    {
        throw new NotImplementedException();
    }

    private void OpenRepository()
    {
        SaveState();
    }






    private void LoadState()
    {
        AccountName    = Settings.Default.AccountName;
        AccountKey     = Settings.Default.AccountKey;
        LocalDirectory = Settings.Default.LocalDirectory;
    }

    private void SaveState()
    {
        Settings.Default.AccountName    = AccountName;
        Settings.Default.AccountKey     = AccountKey;
        Settings.Default.LocalDirectory = LocalDirectory;
        Settings.Default.Save(); // Important: Save the settings
    }
}

public class RepositoryChosenMessage
{
    public RepositoryChosenMessage(RepositoryFacade repository)
    {
        ChosenRepository = repository;
    }

    public RepositoryFacade ChosenRepository { get; }
}