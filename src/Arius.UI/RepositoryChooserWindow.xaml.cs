using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Arius.Core.Facade;
using Arius.UI.Properties;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

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
    private readonly Facade facade;

    public ChooseRepositoryViewModel(Facade facade)
    {
        this.facade = facade;

        LoadState();

        ChooseLocalDirectoryCommand = new RelayCommand(ChooseLocalDirectory);

        LoadContainersCommand       = new RelayCommand(async () => await LoadContainersAsync(), CanLoadContainers);
        SelectLocalDirectoryCommand = new RelayCommand(SelectLocalDirectory);
    }

    public string LocalDirectory
    {
        get => localDirectory;
        set => SetProperty(ref localDirectory, value);
    }

    private string localDirectory;


    public string AccountName
    {
        get => accountName;
        set => SetProperty(ref accountName, value);
    }

    private string accountName;


    public string AccountKey
    {
        get => accountKey;
        set => SetProperty(ref accountKey, value);
    }

    private string accountKey;


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


    private void ChooseLocalDirectory()
    {
        using (var dialog = new FolderBrowserDialog())
        {
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                LocalDirectory = dialog.SelectedPath;
            }
        }
    }
    public ICommand ChooseLocalDirectoryCommand { get; }

    public ICommand LoadContainersCommand       { get; }
    public ICommand SelectLocalDirectoryCommand { get; }

    private bool CanLoadContainers()
    {
        return !string.IsNullOrWhiteSpace(AccountName) && !string.IsNullOrWhiteSpace(AccountKey);
    }

    private async Task LoadContainersAsync()
    {
        var storageFacade = facade.ForStorageAccount(AccountName, AccountKey);
        ContainerNames = new ObservableCollection<string>(await storageFacade.GetContainerNamesAsync().ToListAsync());

        if (ContainerNames.Count > 0)
        {
            SelectedContainerName = ContainerNames[0];
        }

        SaveState();
    }

    private void SelectLocalDirectory()
    {
        // Logic to open a directory picker and set LocalDirectory
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