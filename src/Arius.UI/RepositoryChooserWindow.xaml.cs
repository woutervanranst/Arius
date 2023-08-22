using Arius.Core.Facade;
using Arius.UI.Properties;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MessageBox = System.Windows.MessageBox;

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
}

public partial class ChooseRepositoryViewModel : ObservableObject
{
    private readonly Facade               facade;
    private readonly Debouncer            debouncer = new();


    public ChooseRepositoryViewModel(Facade facade)
    {
        this.facade = facade;

        LoadState();

        SelectLocalDirectoryCommand = new RelayCommand(SelectLocalDirectory);
        OpenRepositoryCommand       = new AsyncRelayCommand(OpenRepositoryAsync);
    }
    

    [ObservableProperty]
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
                    if (CanLoadStorageAccountFacade()) await LoadStorageAccountFacadeAsync();
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
                    if (CanLoadStorageAccountFacade()) await LoadStorageAccountFacadeAsync();
                });
            }
        }
    }
    private string accountKey;

    
    [ObservableProperty]
    private bool storageAccountError;


    private bool CanLoadStorageAccountFacade()
    {
        return !string.IsNullOrWhiteSpace(AccountName) && !string.IsNullOrWhiteSpace(AccountKey);
    }
    private async Task LoadStorageAccountFacadeAsync()
    {
        try
        {
            LoadingRemote = true;

            var storageAccountFacade = facade.ForStorageAccount(AccountName, AccountKey);
            ContainerNames = new ObservableCollection<string>(await storageAccountFacade.GetContainerNamesAsync(0).ToListAsync());

            if (ContainerNames.Contains(Settings.Default.SelectedContainerName))
                SelectedContainerName = Settings.Default.SelectedContainerName;
            else if (ContainerNames.Count > 0)
                SelectedContainerName = ContainerNames[0];

            StorageAccountError  = false;
            StorageAccountFacade = storageAccountFacade;
        }
        catch (Exception e)
        {
            StorageAccountError         = true;
            StorageAccountFacade = default;
        }
        finally
        {
            LoadingRemote = false;
        }
    }
    private StorageAccountFacade? StorageAccountFacade { get; set; } = default;
    

    [ObservableProperty]
    private ObservableCollection<string> containerNames;


    [ObservableProperty]
    private string selectedContainerName;


    [ObservableProperty]
    private string passphrase;


    [ObservableProperty]
    private bool loadingRemote;


    public ICommand OpenRepositoryCommand { get; }
    private async Task OpenRepositoryAsync()
    {
        if (!Directory.Exists(LocalDirectory))
            return;

        if (StorageAccountError)
            return;

        if (string.IsNullOrEmpty(SelectedContainerName))
            return;

        try
        {
            LoadingRemote = true;

            var repositoryFacade = await StorageAccountFacade!.ForRepositoryAsync(SelectedContainerName, Passphrase);

            SaveState();
        }
        catch (ArgumentException e)
        {
            MessageBox.Show("Invalid password");
        }
        catch (Exception e)
        {
        }
        finally
        {
            LoadingRemote = false;
        }
    }



    private void LoadState()
    {
        AccountName    = Settings.Default.AccountName;
        AccountKey     = Settings.Default.AccountKey;
        LocalDirectory = Settings.Default.LocalDirectory;
    }

    private void SaveState()
    {
        Settings.Default.AccountName           = AccountName;
        Settings.Default.AccountKey            = AccountKey;
        Settings.Default.LocalDirectory        = LocalDirectory;
        Settings.Default.SelectedContainerName = SelectedContainerName;
        Settings.Default.Save();
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