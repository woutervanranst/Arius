using System.Windows;
using Microsoft.Extensions.DependencyInjection;

namespace Arius.Explorer.Shared.Services;

public interface IDialogService
{
    TViewModel ShowDialog<TWindow, TViewModel>(Action<TViewModel>? initialize = null)
        where TWindow : Window
        where TViewModel : class;
}

public class DialogService : IDialogService
{
    private readonly IServiceProvider serviceProvider;

    public DialogService(IServiceProvider serviceProvider)
    {
        this.serviceProvider = serviceProvider;
    }

    public TViewModel ShowDialog<TWindow, TViewModel>(Action<TViewModel>? initialize = null)
        where TWindow : Window
        where TViewModel : class
    {
        var window = serviceProvider.GetRequiredService<TWindow>();
        var viewModel = window.DataContext as TViewModel;

        if (viewModel == null)
            throw new InvalidOperationException($"Window {typeof(TWindow).Name} does not have a DataContext of type {typeof(TViewModel).Name}");

        // Initialize the ViewModel
        initialize?.Invoke(viewModel);

        // Set the owner window to the RepositoryExplorer window if it exists
        var owner = Application.Current.Windows.OfType<RepositoryExplorer.Window>().FirstOrDefault();
        if (owner != null)
            window.Owner = owner;

        window.ShowDialog();

        return viewModel;
    }
}