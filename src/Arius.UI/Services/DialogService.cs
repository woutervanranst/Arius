using System.Windows;
using Arius.UI.Views;
using Microsoft.Extensions.DependencyInjection;
using Application = System.Windows.Application;

namespace Arius.UI;

public interface IDialogService
{
    TViewModel ShowDialog<TWindow, TViewModel>(Action<TViewModel> initialize = null)
        where TWindow : Window
        where TViewModel : class;
}

public class DialogService : IDialogService
{
    private readonly IServiceProvider sp;

    public DialogService(IServiceProvider serviceProvider)
    {
        sp = serviceProvider;
    }

    public TViewModel ShowDialog<TWindow, TViewModel>(Action<TViewModel> initialize = null)
        where TWindow : Window
        where TViewModel : class
    {
        var window    = sp.GetRequiredService<TWindow>();
        var viewModel = window.DataContext as TViewModel;

        // Initialize the ViewModel
        initialize?.Invoke(viewModel);

        window.Owner = Application.Current.Windows.OfType<ExploreRepositoryWindow>().Single();
        window.ShowDialog();

        return viewModel;
    }
}
