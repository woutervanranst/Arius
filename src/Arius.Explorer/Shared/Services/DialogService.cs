using Arius.Explorer.ChooseRepository;
using Arius.Explorer.Settings;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;

namespace Arius.Explorer.Shared.Services;

public class DialogService : IDialogService
{
    private readonly IServiceProvider serviceProvider;

    public DialogService(IServiceProvider serviceProvider)
    {
        this.serviceProvider = serviceProvider;
    }

    public RepositoryOptions? ShowChooseRepositoryDialog(RepositoryOptions? initialRepository = null)
    {
        var dialog = serviceProvider.GetRequiredService<ChooseRepository.Window>();

        // Set the initial repository if provided
        if (dialog.DataContext is ChooseRepositoryViewModel viewModel && initialRepository != null)
        {
            viewModel.Repository = initialRepository;
        }

        // Set owner to the main window
        var mainWindow = Application.Current.MainWindow;
        if (mainWindow != null && mainWindow != dialog)
        {
            dialog.Owner = mainWindow;
        }

        // Show dialog and return result
        var dialogResult = dialog.ShowDialog();

        if (dialogResult == true && dialog.DataContext is ChooseRepositoryViewModel resultViewModel)
        {
            return resultViewModel.Repository;
        }

        return null;
    }
}