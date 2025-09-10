using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace Arius.Explorer.ChooseRepository;

public partial class WindowViewModel : ObservableObject
{
    [ObservableProperty]
    private string windowName = "Choose Repository";
    
    [ObservableProperty]
    private bool isLoading;
    
    [ObservableProperty]
    private string localDirectoryPath = "";
    
    [ObservableProperty]
    private string accountName = "";
    
    [ObservableProperty]
    private string accountKey = "";
    
    [ObservableProperty]
    private string containerName = "";
    
    [ObservableProperty]
    private ObservableCollection<string> containerNames = [];
    
    [ObservableProperty]
    private string passphrase = "";
    
    [ObservableProperty]
    private bool storageAccountError;

    public WindowViewModel()
    {
        // Initialize with sample data for development
        LocalDirectoryPath = @"C:\SampleRepository";
        AccountName = "samplestorageaccount";
        ContainerNames = [
            "container1",
            "container2", 
            "backups",
            "archives"
        ];
        ContainerName = "container1";
    }

    [RelayCommand]
    private void SelectLocalDirectory()
    {
        // TODO: Implement folder browser dialog
        LocalDirectoryPath = @"C:\Users\Sample\Documents\MyRepository";
    }

    [RelayCommand]
    private void OpenRepository()
    {
        // TODO: Validate inputs and open repository
        // For now just close the dialog
        IsLoading = true;
        // Simulate some work
        IsLoading = false;
    }
}