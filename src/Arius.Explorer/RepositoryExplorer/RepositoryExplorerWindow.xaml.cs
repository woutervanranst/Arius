using Arius.Explorer.Shared.Messages;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Arius.Explorer.RepositoryExplorer;

/// <summary>
/// Interaction logic for Window.xaml
/// </summary>
public partial class Window : IRecipient<OpenChooseRepositoryDialogMessage>
{
    private readonly ILogger<Window> logger;
    private readonly RepositoryExplorerViewModel viewModel;
    private readonly IServiceProvider serviceProvider;

    public Window(ILogger<Window> logger, RepositoryExplorerViewModel viewModel, IServiceProvider serviceProvider)
    {
        this.logger = logger;
        this.viewModel = viewModel;
        this.serviceProvider = serviceProvider;

        InitializeComponent();
        DataContext = viewModel;

        // Register for messages
        WeakReferenceMessenger.Default.Register<OpenChooseRepositoryDialogMessage>(this);

        logger.LogInformation("Repository Explorer Window initialized");
    }

    public void Receive(OpenChooseRepositoryDialogMessage message)
    {
        // Create and show the ChooseRepository dialog
        var chooseDialog = serviceProvider.GetRequiredService<ChooseRepository.Window>();
        if (chooseDialog.DataContext is ChooseRepository.ChooseRepositoryViewModel chooseViewModel)
        {
            chooseViewModel.Repository = message.InitialRepository;
        }

        chooseDialog.Owner = this;
        chooseDialog.ShowDialog();

        // Handle the result if dialog was successful
        if (chooseDialog.DataContext is ChooseRepository.ChooseRepositoryViewModel resultViewModel &&
            resultViewModel.Repository != null)
        {
            viewModel.HandleRepositorySelected(resultViewModel.Repository);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        WeakReferenceMessenger.Default.Unregister<OpenChooseRepositoryDialogMessage>(this);
        base.OnClosed(e);
    }
}