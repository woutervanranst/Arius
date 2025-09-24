using Microsoft.Extensions.Logging;

namespace Arius.Explorer.RepositoryExplorer;

/// <summary>
/// Interaction logic for ChooseRepositoryWindow.xaml
/// </summary>
public partial class RepositoryExplorerWindow
{
    private readonly ILogger<RepositoryExplorerWindow> logger;

    public RepositoryExplorerWindow(ILogger<RepositoryExplorerWindow> logger, RepositoryExplorerViewModel viewModel)
    {
        this.logger = logger;

        InitializeComponent();
        DataContext = viewModel;

        logger.LogInformation("Repository Explorer ChooseRepositoryWindow initialized");
    }
}